using FoMed.Application.DTO.Clinical;
using FoMed.Application.DTO.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using FoMed.Application.Services.Appointment;

namespace FoMed.Application.Services.Clinical;

public sealed class ClinicalService(ClinicRepository repository, ClinicAccess access, IClinicalAuditContext? auditContext = null)
{
    public async Task<MedicalRecordResponse> CreateRecordAsync(int userId, int appointmentId, SaveMedicalRecordRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var appointment = await repository.Query<Infrastructure.Models.Appointment>().SingleOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy lịch hẹn.");
        if (!await access.IsDoctorAsync(userId, appointment.DoctorId, ct)) throw new ClinicException(403, "Chỉ bác sĩ phụ trách được tạo bệnh án.");
        if (appointment.Status != (byte)AppointmentStatus.Confirmed || !appointment.CheckedInAt.HasValue)
            throw new ClinicException(409, "Bệnh nhân phải được lễ tân check-in trước khi bắt đầu khám.");
        if (await repository.Query<MedicalRecord>().AnyAsync(r => r.AppointmentId == appointmentId, ct)) throw new ClinicException(409, "Lịch hẹn đã có bệnh án.");
        var record = new MedicalRecord
        {
            AppointmentId = appointmentId, PatientId = appointment.PatientId, DoctorId = appointment.DoctorId,
            Symptoms = request.Symptoms, Diagnosis = request.Diagnosis, Note = request.Note,
            VitalsJson = SerializeVitals(request.VitalSigns), Icd10Code = request.Icd10Code,
            TreatmentPlan = request.TreatmentPlan, FollowUpDate = request.FollowUpDate, CreatedAt = DateTime.UtcNow
        };
        repository.Add(record);
        repository.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointmentId, FromStatus = appointment.Status, ToStatus = (byte)AppointmentStatus.InProgress,
            ChangedBy = userId, ChangedAt = DateTime.UtcNow, Reason = "Bắt đầu khám và tạo bệnh án"
        });
        appointment.Status = (byte)AppointmentStatus.InProgress;
        appointment.UpdatedAt = DateTime.UtcNow;
        await repository.SaveAsync(ct);
        await AddAuditAsync(userId, "Create", record.Id, "CreateRecord", ct, ["MedicalRecord"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(record);
    }

    public async Task<MedicalRecordResponse> GetRecordAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await GetReadableAsync(userId, recordId, ct);
        var response = Map(record);
        await AddAuditAsync(userId, "Read", record.Id, "Record", ct);
        await repository.SaveAsync(ct);
        return response;
    }

    public async Task<IReadOnlyList<MedicalRecordResponse>> ListAsync(int userId, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var query = repository.Query<MedicalRecord>().AsNoTracking().Where(r =>
            (r.Patient.UserId == userId && r.Patient.IsActive && r.Patient.User!.IsActive &&
             r.Appointment.Status == (byte)AppointmentStatus.Completed) ||
            (r.Doctor.UserId == userId && r.Doctor.IsActive && r.Doctor.User.IsActive && r.Doctor.User.UserRoles.Any(ur => ur.Role.Name == "Doctor")));
        var list = await query.OrderByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        var response = list.Select(Map).ToList();
        if (list.Count > 0)
        {
            var actor = await GetAuditActorAsync(userId, ct);
            foreach (var record in list) repository.Add(MedicalRecordAudit.Create(userId, actor, "Read", record.Id, "RecordList", auditContext));
        }
        await repository.SaveAsync(ct);
        return response;
    }

    public async Task<IReadOnlyList<PatientHistorySummary>> GetRecordHistoryAsync(int userId, int recordId, CancellationToken ct)
    {
        var context = await repository.Query<MedicalRecord>().AsNoTracking().Include(r => r.Appointment)
            .SingleOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.IsDoctorAsync(userId, context.DoctorId, ct)
            || context.Appointment.DoctorId != context.DoctorId || context.Appointment.PatientId != context.PatientId)
            throw new ClinicException(403, "Chỉ bác sĩ phụ trách được xem lịch sử khám trong lượt khám này.");

        var history = await MedicalRecordHistoryQuery.Recent(repository.Query<MedicalRecord>().AsNoTracking(),
                context.PatientId, context.AppointmentId, context.Appointment.StartTime)
            .Select(r => new PatientHistorySummary(r.Id, r.AppointmentId, r.Appointment.StartTime, r.Diagnosis, r.Note))
            .ToListAsync(ct);
        if (history.Count > 0)
        {
            var actor = await GetAuditActorAsync(userId, ct);
            foreach (var item in history)
                repository.Add(MedicalRecordAudit.Create(userId, actor, "Read", item.MedicalRecordId, "RecordHistory", auditContext));
            await repository.SaveAsync(ct);
        }
        return history;
    }

    public async Task<MedicalRecordResponse> UpdateRecordAsync(int userId, int recordId, SaveMedicalRecordRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        var changedFields = ChangedFields(record, request);
        record.Symptoms = request.Symptoms; record.Diagnosis = request.Diagnosis; record.Note = request.Note;
        record.VitalsJson = SerializeVitals(request.VitalSigns); record.Icd10Code = request.Icd10Code;
        record.TreatmentPlan = request.TreatmentPlan; record.FollowUpDate = request.FollowUpDate;
        record.UpdatedAt = DateTime.UtcNow;
        await AddAuditAsync(userId, "Update", record.Id, "UpdateRecord", ct, changedFields);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(record);
    }

    public async Task<PrescriptionResponse> CreatePrescriptionAsync(int userId, int recordId, CreatePrescriptionRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        ValidatePrescription(request);
        if (await repository.Query<Prescription>().AnyAsync(p => p.MedicalRecordId == recordId, ct)) throw new ClinicException(409, "Bệnh án đã có đơn thuốc.");
        EnsureAllergyAcknowledged(record, request);
        var prescription = new Prescription { MedicalRecordId = recordId, Note = request.Note, CreatedAt = DateTime.UtcNow };
        await FillPrescriptionItemsAsync(prescription, request, ct);
        repository.Add(prescription);
        await AddAuditAsync(userId, "Update", record.Id, "CreatePrescription", ct, ["Prescription"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(prescription);
    }

    public async Task<PrescriptionResponse> UpdatePrescriptionAsync(int userId, int recordId, CreatePrescriptionRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        ValidatePrescription(request);
        EnsureAllergyAcknowledged(record, request);
        var prescription = await repository.Query<Prescription>().SingleOrDefaultAsync(p => p.MedicalRecordId == recordId, ct)
            ?? throw new ClinicException(404, "Chưa có đơn thuốc để cập nhật.");
        if (await PrescriptionDispensingState.HasDispensedAsync(repository, prescription.Id, ct))
            throw new ClinicException(409, "Đơn thuốc đã được cấp phát, không thể chỉnh sửa.");
        await repository.Query<PrescriptionItem>().Where(i => i.PrescriptionId == prescription.Id).ExecuteDeleteAsync(ct);
        prescription.Note = request.Note;
        prescription.PrescriptionItems.Clear();
        await FillPrescriptionItemsAsync(prescription, request, ct);
        await AddAuditAsync(userId, "Update", record.Id, "UpdatePrescription", ct, ["Prescription"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(prescription);
    }

    public async Task<PrescriptionResponse> GetPrescriptionAsync(int userId, int recordId, CancellationToken ct)
    {
        await GetReadableAsync(userId, recordId, ct);
        var prescription = await repository.Query<Prescription>().AsNoTracking().Include(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .SingleOrDefaultAsync(p => p.MedicalRecordId == recordId, ct) ?? throw new ClinicException(404, "Chưa có đơn thuốc.");
        var response = Map(prescription) with { IsDispensed = await PrescriptionDispensingState.HasDispensedAsync(repository, prescription.Id, ct) };
        await AddAuditAsync(userId, "Read", recordId, "Prescription", ct);
        await repository.SaveAsync(ct);
        return response;
    }

    public async Task<ServiceOrderResponse> OrderServiceAsync(int userId, int recordId, OrderServiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        var service = await repository.Query<Service>().SingleOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive, ct)
            ?? throw new ClinicException(404, "Không tìm thấy dịch vụ đang hoạt động.");
        if (await repository.Query<MedicalRecordService>().AnyAsync(o => o.MedicalRecordId == recordId && o.ServiceId == request.ServiceId && o.Status != 2, ct))
            throw new ClinicException(409, "Dịch vụ đã được chỉ định.");
        var order = new MedicalRecordService
        {
            MedicalRecordId = recordId, Service = service, OrderedBy = record.DoctorId,
            Quantity = request.Quantity, UnitPriceSnapshot = service.Price, OrderedAt = DateTime.UtcNow
        };
        repository.Add(order);
        await AddAuditAsync(userId, "Update", record.Id, "OrderService", ct, ["ServiceOrders"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(order);
    }

    public async Task<IReadOnlyList<ServiceOrderResponse>> GetOrdersAsync(int userId, int recordId, CancellationToken ct)
    {
        await GetReadableAsync(userId, recordId, ct);
        var orders = await Orders().Where(o => o.MedicalRecordId == recordId).OrderBy(o => o.Id).ToListAsync(ct);
        await AddAuditAsync(userId, "Read", recordId, "ServiceOrders", ct);
        await repository.SaveAsync(ct);
        return orders.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ServiceOrderResponse>> PendingTestsAsync(int userId, int page, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Technician", ct)) throw new ClinicException(403, "Chỉ kỹ thuật viên được xem hàng đợi xét nghiệm.");
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var orders = await Orders().Where(o => o.Status == 0).OrderBy(o => o.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        if (orders.Count > 0)
        {
            var actor = await GetAuditActorAsync(userId, ct);
            foreach (var recordId in orders.Select(o => o.MedicalRecordId).Distinct())
                repository.Add(MedicalRecordAudit.Create(userId, actor, "Read", recordId, "LabQueue", auditContext));
            await repository.SaveAsync(ct);
        }
        return orders.Select(Map).ToList();
    }

    public async Task<ServiceOrderResponse> SaveResultAsync(int userId, int orderId, SaveLabResultRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        if (!await access.HasRoleAsync(userId, "Technician", ct)) throw new ClinicException(403, "Chỉ kỹ thuật viên được nhập kết quả.");
        var order = await repository.Query<MedicalRecordService>().Include(o => o.Service).Include(o => o.LabResult)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new ClinicException(404, "Không tìm thấy chỉ định.");
        if (order.Status != 0 || order.LabResult != null) throw new ClinicException(409, "Chỉ định đã hoàn thành hoặc bị hủy.");
        order.LabResult = new LabResult
        {
            ResultSummary = request.ResultSummary, Conclusion = request.Conclusion,
            ReferenceRange = request.ReferenceRange, TechnicianId = userId, ResultAt = DateTime.UtcNow
        };
        order.Status = 1;
        await AddAuditAsync(userId, "Update", order.MedicalRecordId, "SaveLabResult", ct, ["LabResults"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(order);
    }

    public async Task<LabResultHistoryPage> LabHistoryAsync(int userId, string? keyword, int page, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Technician", ct)) throw new ClinicException(403, "Chỉ kỹ thuật viên được xem lịch sử kết quả.");
        if (page is < 1 or > 100000 || keyword?.Length > 100) throw new ClinicException(400, "Bộ lọc kết quả không hợp lệ.");
        var query = Orders().AsNoTracking().Include(o => o.MedicalRecord).ThenInclude(r => r.Patient)
            .Where(o => o.Status == 1 && o.LabResult != null && o.LabResult.TechnicianId == userId);
        var term = keyword?.Trim();
        if (!string.IsNullOrEmpty(term)) query = query.Where(o => o.Service.Name.Contains(term) ||
            o.MedicalRecord.Patient.FullName.Contains(term) || o.MedicalRecord.Patient.PatientCode.Contains(term));
        var total = await query.CountAsync(ct);
        var orders = await query.OrderByDescending(o => o.LabResult!.ResultAt).ThenByDescending(o => o.Id).Skip((page - 1) * 10).Take(10).ToListAsync(ct);
        if (orders.Count > 0)
        {
            var actor = await GetAuditActorAsync(userId, ct);
            foreach (var recordId in orders.Select(o => o.MedicalRecordId).Distinct())
                repository.Add(MedicalRecordAudit.Create(userId, actor, "Read", recordId, "LabResultHistory", auditContext));
            await repository.SaveAsync(ct);
        }
        return new(orders.Select(o => new LabResultHistoryItem(Map(o), o.MedicalRecord.Patient.FullName, o.MedicalRecord.Patient.PatientCode)).ToList(), page, 10, total);
    }

    public async Task<ServiceOrderResponse> CancelOrderAsync(int userId, int orderId, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var order = await repository.Query<MedicalRecordService>().Include(o => o.MedicalRecord)
            .Include(o => o.Service).Include(o => o.LabResult)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new ClinicException(404, "Khong tim thay chi dinh.");
        if (!await access.IsDoctorAsync(userId, order.MedicalRecord.DoctorId, ct))
            throw new ClinicException(403, "Chi bac si phu trach duoc huy chi dinh.");
        if (order.Status != 0 || order.LabResult is not null)
            throw new ClinicException(409, "Chi co the huy chi dinh dang cho.");
        order.Status = 2;
        await AddAuditAsync(userId, "Update", order.MedicalRecordId, "CancelServiceOrder", ct, ["ServiceOrders"]);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(order);
    }

    public async Task<IReadOnlyList<CatalogResponse>> CatalogAsync(bool medicines, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        return medicines
            ? await repository.Query<Medicine>().Where(m => m.IsActive).OrderBy(m => m.Id).Skip((page - 1) * 20).Take(20).Select(m => new CatalogResponse(m.Id, m.Name, m.Price)).ToListAsync(ct)
            : await repository.Query<Service>().Where(s => s.IsActive).OrderBy(s => s.Id).Skip((page - 1) * 20).Take(20).Select(s => new CatalogResponse(s.Id, s.Name, s.Price)).ToListAsync(ct);
    }

    private IQueryable<MedicalRecordService> Orders() => repository.Query<MedicalRecordService>().AsNoTracking().Include(o => o.Service).Include(o => o.LabResult);
    public async Task<PrescribingContextResponse> GetPrescribingContextAsync(int userId, int recordId, CancellationToken ct, int[]? medicineIds = null)
    {
        var record = await GetDoctorRecordAsync(userId, recordId, ct);
        if (medicineIds is { Length: > 100 } || medicineIds?.Any(id => id <= 0) == true)
            throw new ClinicException(400, "Danh sách thuốc không hợp lệ.");
        var ids = await repository.Query<PrescriptionItem>().Where(i => i.Prescription.MedicalRecordId == recordId)
            .Select(i => i.MedicineId).ToListAsync(ct);
        ids = ids.Concat(medicineIds ?? []).Distinct().ToList();
        var medicines = await WithAvailableStock(repository.Query<Medicine>().Where(m => ids.Contains(m.Id))).ToListAsync(ct);
        await AddAuditAsync(userId, "Read", recordId, "PrescribingContext", ct);
        await repository.SaveAsync(ct);
        return new(recordId, record.Patient.FullName, record.Patient.Allergies, medicines);
    }

    public async Task<MedicineSearchResponse> SearchMedicinesAsync(int userId, int recordId, string? keyword, int page, CancellationToken ct)
    {
        await GetDoctorRecordAsync(userId, recordId, ct);
        if (page < 1 || page > 100000 || keyword?.Length > 100) throw new ClinicException(400, "Từ khóa hoặc trang không hợp lệ.");
        var term = keyword?.Trim() ?? "";
        var query = repository.Query<Medicine>().AsNoTracking().Where(m => m.IsActive && (term == "" || m.Name.Contains(term)));
        var count = await query.CountAsync(ct);
        var items = await WithAvailableStock(query.OrderBy(m => m.Name).ThenBy(m => m.Id).Skip((page - 1) * 20).Take(20)).ToListAsync(ct);
        return new(items, page, 20, count);
    }

    private IQueryable<PrescribingMedicineResponse> WithAvailableStock(IQueryable<Medicine> query)
    {
        // Same expiry-day rule as dispensing: a date-only batch expires at the end of that clinic day.
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        return query.Select(m => new PrescribingMedicineResponse(m.Id, m.Name, m.Unit, m.Price,
            m.MedicineBatches.Where(b => b.ExpiryDate >= today && b.Quantity > 0).Sum(b => (long)b.Quantity)));
    }

    private async Task<MedicalRecord> GetDoctorRecordAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().Include(r => r.Patient).SingleOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.IsDoctorAsync(userId, record.DoctorId, ct)) throw new ClinicException(403, "Chỉ bác sĩ phụ trách được xem thông tin kê đơn.");
        return record;
    }
    private async Task<MedicalRecord> GetReadableAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().Include(r => r.Appointment).SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.CanReadAsync(userId, record.PatientId, record.DoctorId, ct)) throw new ClinicException(403, "Không có quyền xem bệnh án.");
        if (record.Appointment.Status != (byte)AppointmentStatus.Completed &&
            await access.IsPatientOwnerAsync(userId, record.PatientId, ct))
            throw new ClinicException(403, "Bệnh án chưa được chốt, chưa thể xem từ tài khoản bệnh nhân.");
        return record;
    }
    private async Task<MedicalRecord> GetEditableAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().Include(r => r.Appointment).Include(r => r.Patient).SingleOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.IsDoctorAsync(userId, record.DoctorId, ct)) throw new ClinicException(403, "Chỉ bác sĩ phụ trách được sửa bệnh án.");
        if (record.IsFinalized) throw new ClinicException(409, "Bệnh án đã chốt, không thể sửa.");
        if (record.Appointment.Status != (byte)AppointmentStatus.InProgress) throw new ClinicException(409, "Chỉ được sửa khi đang khám.");
        return record;
    }
    private async Task AddAuditAsync(int userId, string action, int recordId, string source, CancellationToken ct, string[]? changedFields = null)
    {
        var actor = await GetAuditActorAsync(userId, ct);
        repository.Add(MedicalRecordAudit.Create(userId, actor, action, recordId, source, auditContext, changedFields));
    }

    private async Task<User> GetAuditActorAsync(int userId, CancellationToken ct) =>
        await repository.Query<User>().Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw new ClinicException(401, "Không xác định được người thực hiện.");

    private static string[] ChangedFields(MedicalRecord record, SaveMedicalRecordRequest request)
    {
        var fields = new List<string>();
        if (record.Symptoms != request.Symptoms) fields.Add("Symptoms");
        if (record.Diagnosis != request.Diagnosis) fields.Add("Diagnosis");
        if (record.Note != request.Note) fields.Add("Note");
        if (record.VitalsJson != SerializeVitals(request.VitalSigns)) fields.Add("VitalSigns");
        if (record.Icd10Code != request.Icd10Code) fields.Add("Icd10Code");
        if (record.TreatmentPlan != request.TreatmentPlan) fields.Add("TreatmentPlan");
        if (record.FollowUpDate != request.FollowUpDate) fields.Add("FollowUpDate");
        return fields.ToArray();
    }

    private static MedicalRecordResponse Map(MedicalRecord r) => new(r.Id, r.AppointmentId, r.PatientId, r.DoctorId,
        r.Symptoms, r.Diagnosis, r.Note, DeserializeVitals(r.VitalsJson), r.Icd10Code, r.TreatmentPlan,
        r.FollowUpDate, r.IsFinalized, r.FinalizedAt, r.CreatedAt, r.UpdatedAt);
    private static PrescriptionResponse Map(Prescription p) => new(p.Id, p.MedicalRecordId, p.Note, p.PrescriptionItems.Select(i => new PrescriptionLineResponse(i.MedicineId, i.Medicine.Name, i.Quantity, i.UnitPriceSnapshot, i.Dosage, i.Instruction)).ToList());
    private static ServiceOrderResponse Map(MedicalRecordService o) => new(o.Id, o.MedicalRecordId, o.ServiceId, o.Service.Name,
        o.Status, o.Quantity, o.UnitPriceSnapshot, o.LabResult?.ResultSummary, o.LabResult?.Conclusion,
        o.LabResult?.ReferenceRange, o.LabResult is null ? null : DateTime.SpecifyKind(o.LabResult.ResultAt, DateTimeKind.Utc));
    private static string? SerializeVitals(VitalSignsRequest? vitals) => vitals is null ? null : JsonSerializer.Serialize(vitals);
    private static VitalSignsRequest? DeserializeVitals(string? json) => string.IsNullOrWhiteSpace(json)
        ? null : JsonSerializer.Deserialize<VitalSignsRequest>(json);

    private static void ValidatePrescription(CreatePrescriptionRequest request)
    {
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i is null || i.Quantity <= 0) || request.Items.Select(i => i.MedicineId).Distinct().Count() != request.Items.Count)
            throw new ClinicException(400, "Đơn thuốc phải có số lượng dương và không lặp thuốc.");
    }

    private static void EnsureAllergyAcknowledged(MedicalRecord record, CreatePrescriptionRequest request)
    {
        if (!string.IsNullOrWhiteSpace(record.Patient.Allergies) && !request.AllergyAcknowledged)
            throw new ClinicException(409, "Bệnh nhân có thông tin dị ứng. Bác sĩ phải xác nhận đã kiểm tra trước khi kê đơn.");
    }

    private async Task FillPrescriptionItemsAsync(Prescription prescription, CreatePrescriptionRequest request, CancellationToken ct)
    {
        var ids = request.Items.Select(i => i.MedicineId).ToArray();
        var medicines = await repository.Query<Medicine>().Where(m => ids.Contains(m.Id) && m.IsActive).ToDictionaryAsync(m => m.Id, ct);
        if (medicines.Count != ids.Length) throw new ClinicException(404, "Thuốc không tồn tại hoặc đã ngừng sử dụng.");
        var today = DateOnly.FromDateTime(ClinicTime.Now);
        var stocks = await repository.Query<MedicineBatch>().Where(b => ids.Contains(b.MedicineId) && b.Quantity > 0 && b.ExpiryDate >= today)
            .GroupBy(b => b.MedicineId).Select(g => new { Id = g.Key, Quantity = g.Sum(b => (long)b.Quantity) }).ToDictionaryAsync(b => b.Id, b => b.Quantity, ct);
        foreach (var item in request.Items)
        {
            var available = stocks.GetValueOrDefault(item.MedicineId);
            if (item.Quantity > available)
                throw new ClinicException(409, $"Thuốc {medicines[item.MedicineId].Name} chỉ còn {available} trong các lô chưa hết hạn. Vui lòng kiểm tra lại số lượng.");
        }
        foreach (var item in request.Items)
        {
            var medicine = medicines[item.MedicineId];
            prescription.PrescriptionItems.Add(new PrescriptionItem
            {
                Medicine = medicine, Quantity = item.Quantity, UnitPriceSnapshot = medicine.Price,
                Dosage = item.Dosage, Instruction = item.Instruction
            });
        }
    }
}

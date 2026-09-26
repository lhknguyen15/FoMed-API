using FoMed.Application.DTO.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Clinical;

public sealed class ClinicalService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<MedicalRecordResponse> CreateRecordAsync(int userId, int appointmentId, SaveMedicalRecordRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var appointment = await repository.Query<Infrastructure.Models.Appointment>().SingleOrDefaultAsync(a => a.Id == appointmentId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy lịch hẹn.");
        if (!await access.IsDoctorAsync(userId, appointment.DoctorId, ct)) throw new ClinicException(403, "Chỉ bác sĩ phụ trách được tạo bệnh án.");
        if (appointment.Status != (byte)AppointmentStatus.Confirmed) throw new ClinicException(409, "Lịch hẹn phải được xác nhận trước khi bắt đầu khám.");
        if (await repository.Query<MedicalRecord>().AnyAsync(r => r.AppointmentId == appointmentId, ct)) throw new ClinicException(409, "Lịch hẹn đã có bệnh án.");
        var record = new MedicalRecord
        {
            AppointmentId = appointmentId, PatientId = appointment.PatientId, DoctorId = appointment.DoctorId,
            Symptoms = request.Symptoms, Diagnosis = request.Diagnosis, Note = request.Note, CreatedAt = DateTime.UtcNow
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
        await write.CommitAsync(ct);
        return Map(record);
    }

    public async Task<MedicalRecordResponse> GetRecordAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await GetReadableAsync(userId, recordId, ct);
        return Map(record);
    }

    public async Task<IReadOnlyList<MedicalRecordResponse>> ListAsync(int userId, int page, CancellationToken ct)
    {
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        var query = repository.Query<MedicalRecord>().AsNoTracking().Where(r =>
            (r.Patient.UserId == userId && r.Patient.IsActive && r.Patient.User!.IsActive) ||
            (r.Doctor.UserId == userId && r.Doctor.IsActive && r.Doctor.User.IsActive && r.Doctor.User.UserRoles.Any(ur => ur.Role.Name == "Doctor")));
        var list = await query.OrderByDescending(r => r.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct);
        return list.Select(Map).ToList();
    }

    public async Task<MedicalRecordResponse> UpdateRecordAsync(int userId, int recordId, SaveMedicalRecordRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        record.Symptoms = request.Symptoms; record.Diagnosis = request.Diagnosis; record.Note = request.Note;
        record.UpdatedAt = DateTime.UtcNow;
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(record);
    }

    public async Task<PrescriptionResponse> CreatePrescriptionAsync(int userId, int recordId, CreatePrescriptionRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        await GetEditableAsync(userId, recordId, ct);
        if (request.Items is null || request.Items.Count == 0 || request.Items.Any(i => i is null || i.Quantity <= 0) || request.Items.Select(i => i.MedicineId).Distinct().Count() != request.Items.Count)
            throw new ClinicException(400, "Đơn thuốc phải có số lượng dương và không lặp thuốc.");
        if (await repository.Query<Prescription>().AnyAsync(p => p.MedicalRecordId == recordId, ct)) throw new ClinicException(409, "Bệnh án đã có đơn thuốc.");
        var prescription = new Prescription { MedicalRecordId = recordId, Note = request.Note, CreatedAt = DateTime.UtcNow };
        foreach (var item in request.Items)
        {
            var medicine = await repository.Query<Medicine>().SingleOrDefaultAsync(m => m.Id == item.MedicineId && m.IsActive, ct)
                ?? throw new ClinicException(404, "Thuốc không tồn tại hoặc đã ngừng sử dụng.");
            prescription.PrescriptionItems.Add(new PrescriptionItem
            {
                Medicine = medicine, Quantity = item.Quantity, Dosage = item.Dosage, Instruction = item.Instruction
            });
        }
        repository.Add(prescription);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(prescription);
    }

    public async Task<PrescriptionResponse> GetPrescriptionAsync(int userId, int recordId, CancellationToken ct)
    {
        await GetReadableAsync(userId, recordId, ct);
        var prescription = await repository.Query<Prescription>().AsNoTracking().Include(p => p.PrescriptionItems).ThenInclude(i => i.Medicine)
            .SingleOrDefaultAsync(p => p.MedicalRecordId == recordId, ct) ?? throw new ClinicException(404, "Chưa có đơn thuốc.");
        return Map(prescription);
    }

    public async Task<ServiceOrderResponse> OrderServiceAsync(int userId, int recordId, OrderServiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var record = await GetEditableAsync(userId, recordId, ct);
        var service = await repository.Query<Service>().SingleOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive, ct)
            ?? throw new ClinicException(404, "Không tìm thấy dịch vụ đang hoạt động.");
        if (await repository.Query<MedicalRecordService>().AnyAsync(o => o.MedicalRecordId == recordId && o.ServiceId == request.ServiceId && o.Status != 2, ct))
            throw new ClinicException(409, "Dịch vụ đã được chỉ định.");
        var order = new MedicalRecordService { MedicalRecordId = recordId, Service = service, OrderedBy = record.DoctorId, OrderedAt = DateTime.UtcNow };
        repository.Add(order);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(order);
    }

    public async Task<IReadOnlyList<ServiceOrderResponse>> GetOrdersAsync(int userId, int recordId, CancellationToken ct)
    {
        await GetReadableAsync(userId, recordId, ct);
        var orders = await Orders().Where(o => o.MedicalRecordId == recordId).OrderBy(o => o.Id).ToListAsync(ct);
        return orders.Select(Map).ToList();
    }

    public async Task<IReadOnlyList<ServiceOrderResponse>> PendingTestsAsync(int userId, int page, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Technician", ct)) throw new ClinicException(403, "Chỉ kỹ thuật viên được xem hàng đợi xét nghiệm.");
        if (page < 1 || page > 100000) throw new ClinicException(400, "Trang không hợp lệ.");
        return (await Orders().Where(o => o.Status == 0).OrderBy(o => o.Id).Skip((page - 1) * 20).Take(20).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<ServiceOrderResponse> SaveResultAsync(int userId, int orderId, SaveLabResultRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        if (!await access.HasRoleAsync(userId, "Technician", ct)) throw new ClinicException(403, "Chỉ kỹ thuật viên được nhập kết quả.");
        var order = await repository.Query<MedicalRecordService>().Include(o => o.Service).Include(o => o.LabResult)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct) ?? throw new ClinicException(404, "Không tìm thấy chỉ định.");
        if (order.Status != 0 || order.LabResult != null) throw new ClinicException(409, "Chỉ định đã hoàn thành hoặc bị hủy.");
        order.LabResult = new LabResult { ResultSummary = request.ResultSummary, Conclusion = request.Conclusion, TechnicianId = userId, ResultAt = DateTime.UtcNow };
        order.Status = 1;
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
    private async Task<MedicalRecord> GetReadableAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().SingleOrDefaultAsync(r => r.Id == recordId, ct) ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.CanReadAsync(userId, record.PatientId, record.DoctorId, ct)) throw new ClinicException(403, "Không có quyền xem bệnh án.");
        return record;
    }
    private async Task<MedicalRecord> GetEditableAsync(int userId, int recordId, CancellationToken ct)
    {
        var record = await repository.Query<MedicalRecord>().Include(r => r.Appointment).SingleOrDefaultAsync(r => r.Id == recordId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy bệnh án.");
        if (!await access.IsDoctorAsync(userId, record.DoctorId, ct)) throw new ClinicException(403, "Chỉ bác sĩ phụ trách được sửa bệnh án.");
        if (record.Appointment.Status != (byte)AppointmentStatus.InProgress) throw new ClinicException(409, "Chỉ được sửa khi đang khám.");
        return record;
    }
    private static MedicalRecordResponse Map(MedicalRecord r) => new(r.Id, r.AppointmentId, r.PatientId, r.DoctorId, r.Symptoms, r.Diagnosis, r.Note, r.CreatedAt, r.UpdatedAt);
    private static PrescriptionResponse Map(Prescription p) => new(p.Id, p.MedicalRecordId, p.Note, p.PrescriptionItems.Select(i => new PrescriptionLineResponse(i.MedicineId, i.Medicine.Name, i.Quantity, i.Dosage, i.Instruction)).ToList());
    private static ServiceOrderResponse Map(MedicalRecordService o) => new(o.Id, o.MedicalRecordId, o.ServiceId, o.Service.Name, o.Status, o.LabResult?.ResultSummary, o.LabResult?.Conclusion, o.LabResult?.ResultAt);
}

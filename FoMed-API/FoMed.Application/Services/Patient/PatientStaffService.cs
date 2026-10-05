using System.Text.Json;
using FoMed.Application.Services.Clinical;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using PatientEntity = FoMed.Infrastructure.Models.Patient;
using AppointmentEntity = FoMed.Infrastructure.Models.Appointment;

namespace FoMed.Application.Services.Patient;

public sealed class PatientStaffService(ClinicRepository repository, IClinicalAuditContext? auditContext = null)
{
    public async Task<HTTPResponseData<IReadOnlyList<PatientResponse>>> SearchAsync(
        int userId, PatientStaffSearchRequest request, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) return Fail<IReadOnlyList<PatientResponse>>(PatientResponseMessageDTO.StaffOnly, 403);
        if (request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 100)
            return Fail<IReadOnlyList<PatientResponse>>("Trang hoặc kích thước trang không hợp lệ.", 400);

        var query = repository.Query<PatientEntity>().AsNoTracking().Where(p => p.IsActive);
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : PatientService.NormalizePhone(request.Phone);
        if (phone is not null) query = query.Where(p => p.Phone == phone);
        if (!string.IsNullOrWhiteSpace(request.Name)) query = query.Where(p => p.FullName.Contains(request.Name.Trim()));
        if (!string.IsNullOrWhiteSpace(request.PatientCode)) query = query.Where(p => p.PatientCode == request.PatientCode.Trim());

        var patients = await query.OrderBy(p => p.FullName)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        IReadOnlyList<PatientResponse> data = patients.Select(Map).ToList();
        return Success(data, "Lấy danh sách bệnh nhân tại quầy thành công.");
    }

    public async Task<HTTPResponseData<PatientResponse?>> GetAsync(int userId, int patientId, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) return Fail<PatientResponse?>(PatientResponseMessageDTO.StaffOnly, 403);
        var patient = await repository.Query<PatientEntity>().AsNoTracking().SingleOrDefaultAsync(p => p.Id == patientId && p.IsActive, ct);
        return patient is null
            ? Fail<PatientResponse?>(PatientResponseMessageDTO.PatientNotFound, 404)
            : Success<PatientResponse?>(Map(patient), PatientResponseMessageDTO.GetPatientSuccess);
    }

    public async Task<HTTPResponseData<PatientResponse?>> CreateAsync(int userId, CreateWalkInPatientRequest request, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) return Fail<PatientResponse?>(PatientResponseMessageDTO.StaffOnly, 403);
        var phone = PatientService.NormalizePhone(request.Phone);
        if (string.IsNullOrWhiteSpace(phone)) return Fail<PatientResponse?>(PatientResponseMessageDTO.PatientPhoneRequired, 400);
        if (await repository.Query<PatientEntity>().AnyAsync(p => p.Phone == phone && p.IsActive, ct))
            return Fail<PatientResponse?>(PatientResponseMessageDTO.PatientAlreadyExists, 409);

        await using var write = await repository.BeginWriteAsync(ct);
        var now = DateTime.UtcNow;
        var patient = new PatientEntity
        {
            PatientCode = await repository.NextPatientCodeAsync(ct),
            FullName = request.FullName.Trim(), Phone = phone, Gender = request.Gender,
            DateOfBirth = request.DateOfBirth, Address = Normalize(request.Address),
            NationalId = Normalize(request.NationalId), InsuranceNumber = Normalize(request.InsuranceNumber),
            EmergencyContactName = Normalize(request.EmergencyContactName),
            EmergencyContactPhone = NormalizePhoneOptional(request.EmergencyContactPhone),
            Allergies = Normalize(request.Allergies), IsActive = true, CreatedAt = now
        };
        repository.Add(patient);
        await repository.SaveAsync(ct);
        AddAudit(userId, "Create", patient.Id, null, patient);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Success<PatientResponse?>(Map(patient), PatientResponseMessageDTO.PatientCreatedSuccess, 201);
    }

    public async Task<HTTPResponseData<PatientResponse?>> UpdateAsync(int userId, int patientId, UpdatePatientProfileRequest request, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) return Fail<PatientResponse?>(PatientResponseMessageDTO.StaffOnly, 403);
        await using var write = await repository.BeginWriteAsync(ct);
        var patient = await repository.Query<PatientEntity>().SingleOrDefaultAsync(p => p.Id == patientId && p.IsActive, ct);
        if (patient is null) return Fail<PatientResponse?>(PatientResponseMessageDTO.PatientNotFound, 404);
        var oldValue = JsonSerializer.Serialize(Map(patient));
        patient.FullName = request.FullName.Trim(); patient.Gender = request.Gender; patient.DateOfBirth = request.DateOfBirth;
        patient.Phone = NormalizePhoneOptional(request.Phone); patient.Address = Normalize(request.Address);
        patient.NationalId = Normalize(request.NationalId); patient.InsuranceNumber = Normalize(request.InsuranceNumber);
        patient.EmergencyContactName = Normalize(request.EmergencyContactName);
        patient.EmergencyContactPhone = NormalizePhoneOptional(request.EmergencyContactPhone);
        patient.Allergies = Normalize(request.Allergies);
        AddAudit(userId, "Update", patient.Id, oldValue, JsonSerializer.Serialize(Map(patient)));
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Success<PatientResponse?>(Map(patient), PatientResponseMessageDTO.UpdatePatientSuccess);
    }

    public async Task<HTTPResponseData<IReadOnlyList<PatientHistoryResponse>>> HistoryAsync(int userId, int patientId, CancellationToken ct)
    {
        if (!await IsStaffAsync(userId, ct)) return Fail<IReadOnlyList<PatientHistoryResponse>>(PatientResponseMessageDTO.StaffOnly, 403);
        if (!await repository.Query<PatientEntity>().AnyAsync(p => p.Id == patientId && p.IsActive, ct))
            return Fail<IReadOnlyList<PatientHistoryResponse>>(PatientResponseMessageDTO.PatientNotFound, 404);
        var appointments = await repository.Query<AppointmentEntity>().AsNoTracking()
            .Include(a => a.Doctor).Include(a => a.Service).Include(a => a.MedicalRecord).Where(a => a.PatientId == patientId)
            .OrderByDescending(a => a.StartTime).Take(100).ToListAsync(ct);
        var response = appointments.Select(a => new PatientHistoryResponse(
            a.Id, a.AppointmentCode, a.StartTime, a.Status, ((AppointmentStatus)a.Status).ToString(),
            a.DoctorId, a.Doctor.FullName, a.ServiceId, a.Service?.Name,
            a.MedicalRecord?.Id, a.MedicalRecord?.Diagnosis)).ToList();
        repository.Add(new AuditLog
        {
            UserId = userId, Action = "Read", Entity = "PatientHistory", EntityId = patientId,
            NewValue = $"patientId={patientId}", CreatedAt = DateTime.UtcNow
        });
        var recordIds = appointments.Where(a => a.MedicalRecord is not null).Select(a => a.MedicalRecord!.Id).Distinct().ToArray();
        if (recordIds.Length > 0)
        {
            var actor = await repository.Query<User>().Include(u => u.UserRoles).ThenInclude(r => r.Role).SingleAsync(u => u.Id == userId, ct);
            foreach (var recordId in recordIds)
                repository.Add(MedicalRecordAudit.Create(userId, actor, "Read", recordId, "PatientHistorySummary", auditContext));
        }
        await repository.SaveAsync(ct);
        return Success<IReadOnlyList<PatientHistoryResponse>>(response, PatientResponseMessageDTO.PatientHistorySuccess);
    }

    private Task<bool> IsStaffAsync(int userId, CancellationToken ct) => repository.Query<User>()
        .AnyAsync(u => u.Id == userId && u.IsActive && u.UserRoles.Any(ur => ur.Role.Name == "Receptionist" || ur.Role.Name == "Admin"), ct);

    private void AddAudit(int userId, string action, int entityId, string? oldValue, object? newValue) => repository.Add(new AuditLog
    {
        UserId = userId, Action = action, Entity = "Patient", EntityId = entityId,
        OldValue = oldValue, NewValue = newValue is string text ? text : JsonSerializer.Serialize(newValue), CreatedAt = DateTime.UtcNow
    });

    private static PatientResponse Map(PatientEntity patient) => new(patient.Id, patient.UserId, patient.PatientCode, patient.FullName,
        patient.Gender, patient.DateOfBirth, patient.Phone, patient.Address, patient.NationalId, patient.InsuranceNumber,
        patient.EmergencyContactName, patient.EmergencyContactPhone, patient.Allergies, patient.IsActive);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizePhoneOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : PatientService.NormalizePhone(value);
    private static HTTPResponseData<T> Success<T>(T data, string message, int status = 200) => new() { DataResponse = data, Message = message, StatusCode = status };
    private static HTTPResponseData<T> Fail<T>(string message, int status) => new() { DataResponse = default!, Message = message, StatusCode = status };
}

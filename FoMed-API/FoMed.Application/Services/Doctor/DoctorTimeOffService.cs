using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using DoctorEntity = FoMed.Infrastructure.Models.Doctor;
using AppointmentEntity = FoMed.Infrastructure.Models.Appointment;

namespace FoMed.Application.Services.Doctor;

public sealed class DoctorTimeOffService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<IReadOnlyList<DoctorTimeOffResponse>> ListAsync(int userId, bool admin, int? doctorId, CancellationToken ct)
    {
        var target = admin ? doctorId : await OwnDoctorIdAsync(userId, ct);
        if (admin) await RequireAdminAsync(userId, ct);
        var query = repository.Query<DoctorTimeOff>().AsNoTracking().Where(x => admin ? (!doctorId.HasValue || x.DoctorId == doctorId) : x.DoctorId == target);
        return (await query.OrderBy(x => x.StartAt).ToListAsync(ct)).Select(Map).ToList();
    }

    public async Task<DoctorTimeOffResponse> CreateAsync(int userId, SaveDoctorTimeOffRequest request, bool admin, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var doctorId = await ResolveDoctorIdAsync(userId, request.DoctorId, admin, ct);
        var (start, end) = ValidateRange(request);
        await EnsureNoAppointmentConflictAsync(doctorId, start, end, ct);
        var timeOff = new DoctorTimeOff { DoctorId = doctorId, StartAt = start, EndAt = end, Reason = Normalize(request.Reason) };
        repository.Add(timeOff); repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "DoctorTimeOff", NewValue = $"doctorId={doctorId};start={start:o};end={end:o}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return Map(timeOff);
    }

    public async Task<DoctorTimeOffResponse> UpdateAsync(int userId, int id, SaveDoctorTimeOffRequest request, bool admin, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var timeOff = await repository.Query<DoctorTimeOff>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch nghỉ.");
        var doctorId = await ResolveDoctorIdAsync(userId, request.DoctorId ?? timeOff.DoctorId, admin, ct);
        var (start, end) = ValidateRange(request);
        await EnsureNoAppointmentConflictAsync(doctorId, start, end, ct);
        timeOff.DoctorId = doctorId; timeOff.StartAt = start; timeOff.EndAt = end; timeOff.Reason = Normalize(request.Reason);
        repository.Add(new AuditLog { UserId = userId, Action = "Update", Entity = "DoctorTimeOff", EntityId = id, CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return Map(timeOff);
    }

    public async Task DeleteAsync(int userId, int id, bool admin, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct);
        var timeOff = await repository.Query<DoctorTimeOff>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch nghỉ.");
        if (!admin && timeOff.DoctorId != await OwnDoctorIdAsync(userId, ct)) throw new ClinicException(403, "Không có quyền xóa lịch nghỉ này.");
        if (admin) await RequireAdminAsync(userId, ct);
        repository.Remove(timeOff); repository.Add(new AuditLog { UserId = userId, Action = "Delete", Entity = "DoctorTimeOff", EntityId = id, CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
    }

    private async Task<int?> ResolveDoctorIdAsync(int userId, int? requested, bool admin, CancellationToken ct)
    {
        if (admin) { await RequireAdminAsync(userId, ct); return requested; }
        var own = await OwnDoctorIdAsync(userId, ct);
        if (requested.HasValue && requested != own) throw new ClinicException(403, "Bác sĩ chỉ được quản lý lịch nghỉ của mình.");
        return own;
    }
    private async Task<int> OwnDoctorIdAsync(int userId, CancellationToken ct) => await repository.Query<DoctorEntity>().Where(d => d.UserId == userId && d.IsActive).Select(d => (int?)d.Id).SingleOrDefaultAsync(ct) ?? throw new ClinicException(404, "Không tìm thấy hồ sơ bác sĩ.");
    private async Task RequireAdminAsync(int userId, CancellationToken ct) { if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý lịch nghỉ toàn phòng khám."); }
    private async Task EnsureNoAppointmentConflictAsync(int? doctorId, DateTime start, DateTime end, CancellationToken ct)
    {
        var query = repository.Query<AppointmentEntity>().Where(a => a.Status < 3 && a.StartTime < end && a.EndTime > start);
        if (doctorId.HasValue) query = query.Where(a => a.DoctorId == doctorId.Value);
        if (await query.AnyAsync(ct)) throw new ClinicException(409, "Khoảng nghỉ đang có lịch hẹn, cần xử lý lịch bị ảnh hưởng trước.");
    }
    private static (DateTime Start, DateTime End) ValidateRange(SaveDoctorTimeOffRequest request)
    {
        var start = ClinicTime.Normalize(request.StartAt); var end = ClinicTime.Normalize(request.EndAt);
        if (end <= start) throw new ClinicException(400, "Thời gian nghỉ không hợp lệ.");
        return (start, end);
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static DoctorTimeOffResponse Map(DoctorTimeOff x) => new(x.Id, x.DoctorId, x.StartAt, x.EndAt, x.Reason);
}

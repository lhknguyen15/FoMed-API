using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Doctor;

public sealed class AdminDoctorScheduleService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<IReadOnlyList<AdminDoctorScheduleResponse>> ListAsync(int userId, int? doctorId, CancellationToken ct, bool reception = false)
    {
        await RequireManagerAsync(userId, reception, ct);
        var query = repository.Query<DoctorSchedule>().AsNoTracking().Include(x => x.Doctor).AsQueryable();
        if (doctorId.HasValue) query = query.Where(x => x.DoctorId == doctorId.Value);
        return await query.OrderBy(x => x.DoctorId).ThenBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new AdminDoctorScheduleResponse(x.Id, x.DoctorId, x.Doctor.FullName, x.DayOfWeek, x.StartTime, x.EndTime, x.SlotMinutes, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AdminDoctorScheduleResponse> CreateAsync(int userId, SaveAdminDoctorScheduleRequest request, CancellationToken ct, bool reception = false)
    {
        await RequireManagerAsync(userId, reception, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireManagerAsync(userId, reception, ct);
        await ValidateAsync(request, null, ct);
        var schedule = new DoctorSchedule { DoctorId = request.DoctorId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime, EndTime = request.EndTime, SlotMinutes = request.SlotMinutes, IsActive = request.IsActive };
        repository.Add(schedule);
        await repository.SaveAsync(ct);
        repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "DoctorSchedule", EntityId = schedule.Id, NewValue = Snapshot(schedule), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return await GetResponseAsync(schedule.Id, ct);
    }

    public async Task<IReadOnlyList<AdminDoctorScheduleResponse>> CreateBatchAsync(int userId, SaveAdminDoctorScheduleBatchRequest request, CancellationToken ct, bool reception = false)
    {
        await RequireManagerAsync(userId, reception, ct);
        if (request.DayOfWeeks is null || request.DayOfWeeks.Count == 0 || request.DayOfWeeks.Any(day => day > 6) || request.DayOfWeeks.Distinct().Count() != request.DayOfWeeks.Count)
            throw new ClinicException(400, "Hãy chọn ít nhất một ngày trong tuần, không chọn lặp ngày.");

        await using var write = await repository.BeginWriteAsync(ct);
        await RequireManagerAsync(userId, reception, ct);
        var schedules = new List<DoctorSchedule>();
        foreach (var day in request.DayOfWeeks.Order())
        {
            var oneDay = new SaveAdminDoctorScheduleRequest
            {
                DoctorId = request.DoctorId,
                DayOfWeek = day,
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                SlotMinutes = request.SlotMinutes,
                IsActive = request.IsActive
            };
            await ValidateAsync(oneDay, null, ct);
            var schedule = new DoctorSchedule
            {
                DoctorId = oneDay.DoctorId,
                DayOfWeek = oneDay.DayOfWeek,
                StartTime = oneDay.StartTime,
                EndTime = oneDay.EndTime,
                SlotMinutes = oneDay.SlotMinutes,
                IsActive = oneDay.IsActive
            };
            schedules.Add(schedule);
            repository.Add(schedule);
        }
        await repository.SaveAsync(ct);
        foreach (var schedule in schedules)
            repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "DoctorSchedule", EntityId = schedule.Id, NewValue = Snapshot(schedule), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        var ids = schedules.Select(schedule => schedule.Id).ToArray();
        return await repository.Query<DoctorSchedule>().AsNoTracking().Include(schedule => schedule.Doctor)
            .Where(schedule => ids.Contains(schedule.Id))
            .OrderBy(schedule => schedule.DayOfWeek)
            .Select(schedule => new AdminDoctorScheduleResponse(schedule.Id, schedule.DoctorId, schedule.Doctor.FullName, schedule.DayOfWeek, schedule.StartTime, schedule.EndTime, schedule.SlotMinutes, schedule.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AdminDoctorScheduleResponse> UpdateAsync(int userId, int id, SaveAdminDoctorScheduleRequest request, CancellationToken ct, bool reception = false)
    {
        await RequireManagerAsync(userId, reception, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireManagerAsync(userId, reception, ct);
        var schedule = await repository.Query<DoctorSchedule>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch làm việc.");
        CheckVersion(schedule, request.ExpectedVersion, reception);
        await ValidateAsync(request, id, ct);
        await DoctorScheduleSafety.EnsureAsync(repository, schedule, new DoctorSchedule { DoctorId = request.DoctorId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime, EndTime = request.EndTime, SlotMinutes = request.SlotMinutes, IsActive = request.IsActive }, ct);
        var oldValue = Snapshot(schedule);
        schedule.DoctorId = request.DoctorId; schedule.DayOfWeek = request.DayOfWeek; schedule.StartTime = request.StartTime; schedule.EndTime = request.EndTime; schedule.SlotMinutes = request.SlotMinutes; schedule.IsActive = request.IsActive;
        repository.Add(new AuditLog { UserId = userId, Action = "Update", Entity = "DoctorSchedule", EntityId = id, OldValue = oldValue, NewValue = Snapshot(schedule), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return await GetResponseAsync(id, ct);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct, bool reception = false, string? expectedVersion = null)
    {
        await RequireManagerAsync(userId, reception, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await RequireManagerAsync(userId, reception, ct);
        var schedule = await repository.Query<DoctorSchedule>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch làm việc.");
        CheckVersion(schedule, expectedVersion, reception);
        if (!schedule.IsActive) return;
        await DoctorScheduleSafety.EnsureAsync(repository, schedule, new DoctorSchedule(), ct);
        var oldValue = Snapshot(schedule);
        schedule.IsActive = false;
        repository.Add(new AuditLog { UserId = userId, Action = "Deactivate", Entity = "DoctorSchedule", EntityId = id, OldValue = oldValue, NewValue = Snapshot(schedule), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
    }

    private async Task ValidateAsync(SaveAdminDoctorScheduleRequest request, int? excludeId, CancellationToken ct)
    {
        if (!await repository.Query<FoMed.Infrastructure.Models.Doctor>().AnyAsync(x => x.Id == request.DoctorId && x.IsActive, ct)) throw new ClinicException(404, "Không tìm thấy bác sĩ đang hoạt động.");
        if (request.DayOfWeek > 6 || request.EndTime <= request.StartTime || request.SlotMinutes is < 5 or > 240 ||
            request.StartTime.Ticks % TimeSpan.TicksPerMinute != 0 || request.EndTime.Ticks % TimeSpan.TicksPerMinute != 0 ||
            (request.EndTime - request.StartTime).TotalMinutes < request.SlotMinutes)
            throw new ClinicException(400, "Ngày và giờ làm việc phải hợp lệ, không có giây; mỗi lượt từ 5 đến 240 phút và phải nằm trọn trong ca.");
        if (request.IsActive && await repository.Query<DoctorSchedule>().AnyAsync(x => x.DoctorId == request.DoctorId && x.IsActive && x.DayOfWeek == request.DayOfWeek && x.StartTime < request.EndTime && x.EndTime > request.StartTime && (!excludeId.HasValue || x.Id != excludeId), ct)) throw new ClinicException(409, "Khung giờ bị trùng với lịch làm việc khác của bác sĩ.");
    }

    private async Task RequireManagerAsync(int userId, bool reception, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Admin", ct) && !(reception && await access.HasRoleAsync(userId, "Receptionist", ct)))
            throw new ClinicException(403, "Bạn không có quyền quản lý lịch làm việc bác sĩ.");
    }

    public async Task<IReadOnlyList<ScheduleDoctorChoice>> ReceptionDoctorsAsync(int userId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, true, ct);
        return await repository.Query<FoMed.Infrastructure.Models.Doctor>().AsNoTracking().OrderBy(d => d.FullName).ThenBy(d => d.Id)
            .Select(d => new ScheduleDoctorChoice(d.Id, d.FullName, d.Title, d.IsActive)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DoctorTimeOffResponse>> ReceptionTimeOffAsync(int userId, int? doctorId, CancellationToken ct)
    {
        await RequireManagerAsync(userId, true, ct);
        var query = repository.Query<DoctorTimeOff>().AsNoTracking();
        if (doctorId.HasValue) query = query.Where(d => d.DoctorId == null || d.DoctorId == doctorId);
        return await query.OrderBy(d => d.StartAt).ThenBy(d => d.Id)
            .Select(d => new DoctorTimeOffResponse(d.Id, d.DoctorId, d.StartAt, d.EndAt, d.Reason)).ToListAsync(ct);
    }

    private static string Snapshot(DoctorSchedule s) => $"doctorId={s.DoctorId};day={s.DayOfWeek};startTicks={s.StartTime.Ticks};endTicks={s.EndTime.Ticks};slot={s.SlotMinutes};active={s.IsActive}";
    internal static string Version(int id, int doctorId, byte day, TimeOnly start, TimeOnly end, int slot, bool active) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            FormattableString.Invariant($"{id}|{doctorId}|{day}|{start.Ticks}|{end.Ticks}|{slot}|{active}"))));
    private static void CheckVersion(DoctorSchedule s, string? expected, bool required)
    {
        if (required && string.IsNullOrWhiteSpace(expected)) throw new ClinicException(400, "Hãy tải lại lịch làm việc trước khi lưu thay đổi.");
        if (expected is not null && expected != Version(s.Id, s.DoctorId, s.DayOfWeek, s.StartTime, s.EndTime, s.SlotMinutes, s.IsActive))
            throw new ClinicException(409, "Ca làm việc đã được người khác thay đổi. Hãy tải lại danh sách và kiểm tra trước khi lưu.");
    }

    private Task<AdminDoctorScheduleResponse> GetResponseAsync(int id, CancellationToken ct) => repository.Query<DoctorSchedule>().AsNoTracking().Include(x => x.Doctor)
        .Where(x => x.Id == id).Select(x => new AdminDoctorScheduleResponse(x.Id, x.DoctorId, x.Doctor.FullName, x.DayOfWeek, x.StartTime, x.EndTime, x.SlotMinutes, x.IsActive)).SingleAsync(ct);
}

public sealed record ScheduleDoctorChoice(int DoctorId, string FullName, string? Title, bool IsActive);
public sealed record AdminDoctorScheduleResponse(int Id, int DoctorId, string DoctorName, byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes, bool IsActive)
{
    public string Version => AdminDoctorScheduleService.Version(Id, DoctorId, DayOfWeek, StartTime, EndTime, SlotMinutes, IsActive);
}

public sealed record SaveAdminDoctorScheduleRequest
{
    public string? ExpectedVersion { get; init; }
    public int DoctorId { get; init; }
    public byte DayOfWeek { get; init; }
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public int SlotMinutes { get; init; } = 30;
    public bool IsActive { get; init; } = true;
}

public sealed record SaveAdminDoctorScheduleBatchRequest
{
    public int DoctorId { get; init; }
    public IReadOnlyList<byte> DayOfWeeks { get; init; } = [];
    public TimeOnly StartTime { get; init; }
    public TimeOnly EndTime { get; init; }
    public int SlotMinutes { get; init; } = 30;
    public bool IsActive { get; init; } = true;
}

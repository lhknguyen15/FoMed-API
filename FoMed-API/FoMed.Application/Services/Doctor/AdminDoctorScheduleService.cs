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
    public async Task<IReadOnlyList<AdminDoctorScheduleResponse>> ListAsync(int userId, int? doctorId, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var query = repository.Query<DoctorSchedule>().AsNoTracking().Include(x => x.Doctor).AsQueryable();
        if (doctorId.HasValue) query = query.Where(x => x.DoctorId == doctorId.Value);
        return await query.OrderBy(x => x.DoctorId).ThenBy(x => x.DayOfWeek).ThenBy(x => x.StartTime)
            .Select(x => new AdminDoctorScheduleResponse(x.Id, x.DoctorId, x.Doctor.FullName, x.DayOfWeek, x.StartTime, x.EndTime, x.SlotMinutes, x.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AdminDoctorScheduleResponse> CreateAsync(int userId, SaveAdminDoctorScheduleRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        await ValidateAsync(request, null, ct);
        var schedule = new DoctorSchedule { DoctorId = request.DoctorId, DayOfWeek = request.DayOfWeek, StartTime = request.StartTime, EndTime = request.EndTime, SlotMinutes = request.SlotMinutes, IsActive = request.IsActive };
        repository.Add(schedule);
        repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "DoctorSchedule", NewValue = $"doctorId={schedule.DoctorId};day={schedule.DayOfWeek};start={schedule.StartTime};end={schedule.EndTime}", CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return await GetResponseAsync(schedule.Id, ct);
    }

    public async Task<IReadOnlyList<AdminDoctorScheduleResponse>> CreateBatchAsync(int userId, SaveAdminDoctorScheduleBatchRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        if (request.DayOfWeeks is null || request.DayOfWeeks.Count == 0 || request.DayOfWeeks.Any(day => day > 6) || request.DayOfWeeks.Distinct().Count() != request.DayOfWeeks.Count)
            throw new ClinicException(400, "Hãy chọn ít nhất một ngày trong tuần, không chọn lặp ngày.");

        await using var write = await repository.BeginWriteAsync(ct);
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
            repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "DoctorSchedule", NewValue = $"doctorId={schedule.DoctorId};day={schedule.DayOfWeek};start={schedule.StartTime};end={schedule.EndTime}", CreatedAt = DateTime.UtcNow });
        }
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        var ids = schedules.Select(schedule => schedule.Id).ToArray();
        return await repository.Query<DoctorSchedule>().AsNoTracking().Include(schedule => schedule.Doctor)
            .Where(schedule => ids.Contains(schedule.Id))
            .OrderBy(schedule => schedule.DayOfWeek)
            .Select(schedule => new AdminDoctorScheduleResponse(schedule.Id, schedule.DoctorId, schedule.Doctor.FullName, schedule.DayOfWeek, schedule.StartTime, schedule.EndTime, schedule.SlotMinutes, schedule.IsActive))
            .ToListAsync(ct);
    }

    public async Task<AdminDoctorScheduleResponse> UpdateAsync(int userId, int id, SaveAdminDoctorScheduleRequest request, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        var schedule = await repository.Query<DoctorSchedule>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch làm việc.");
        await ValidateAsync(request, id, ct);
        schedule.DoctorId = request.DoctorId; schedule.DayOfWeek = request.DayOfWeek; schedule.StartTime = request.StartTime; schedule.EndTime = request.EndTime; schedule.SlotMinutes = request.SlotMinutes; schedule.IsActive = request.IsActive;
        repository.Add(new AuditLog { UserId = userId, Action = "Update", Entity = "DoctorSchedule", EntityId = id, CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return await GetResponseAsync(id, ct);
    }

    public async Task DeleteAsync(int userId, int id, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        await using var write = await repository.BeginWriteAsync(ct);
        var schedule = await repository.Query<DoctorSchedule>().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy lịch làm việc.");
        schedule.IsActive = false;
        repository.Add(new AuditLog { UserId = userId, Action = "Delete", Entity = "DoctorSchedule", EntityId = id, CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
    }

    private async Task ValidateAsync(SaveAdminDoctorScheduleRequest request, int? excludeId, CancellationToken ct)
    {
        if (!await repository.Query<FoMed.Infrastructure.Models.Doctor>().AnyAsync(x => x.Id == request.DoctorId && x.IsActive, ct)) throw new ClinicException(404, "Không tìm thấy bác sĩ đang hoạt động.");
        if (request.DayOfWeek > 6 || request.EndTime <= request.StartTime || request.SlotMinutes is < 5 or > 240) throw new ClinicException(400, "Ngày, giờ làm việc hoặc thời lượng mỗi lượt không hợp lệ.");
        if (request.IsActive && await repository.Query<DoctorSchedule>().AnyAsync(x => x.DoctorId == request.DoctorId && x.IsActive && x.DayOfWeek == request.DayOfWeek && x.StartTime < request.EndTime && x.EndTime > request.StartTime && (!excludeId.HasValue || x.Id != excludeId), ct)) throw new ClinicException(409, "Khung giờ bị trùng với lịch làm việc khác của bác sĩ.");
    }

    private async Task RequireAdminAsync(int userId, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý lịch làm việc.");
    }

    private Task<AdminDoctorScheduleResponse> GetResponseAsync(int id, CancellationToken ct) => repository.Query<DoctorSchedule>().AsNoTracking().Include(x => x.Doctor)
        .Where(x => x.Id == id).Select(x => new AdminDoctorScheduleResponse(x.Id, x.DoctorId, x.Doctor.FullName, x.DayOfWeek, x.StartTime, x.EndTime, x.SlotMinutes, x.IsActive)).SingleAsync(ct);
}

public sealed record AdminDoctorScheduleResponse(int Id, int DoctorId, string DoctorName, byte DayOfWeek, TimeOnly StartTime, TimeOnly EndTime, int SlotMinutes, bool IsActive);

public sealed record SaveAdminDoctorScheduleRequest
{
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

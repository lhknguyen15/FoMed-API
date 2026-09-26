using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IDoctorScheduleRepository : IRepositoryBase<DoctorSchedule>
{
    Task<IReadOnlyList<DoctorSchedule>> GetByDoctorIdAsync(
        int doctorId,
        CancellationToken cancellationToken = default);

    Task<DoctorSchedule?> GetByIdAndDoctorIdAsync(
        int scheduleId,
        int doctorId,
        CancellationToken cancellationToken = default);

    Task<bool> HasOverlapAsync(
        int doctorId,
        byte dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        int? excludedScheduleId = null,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorScheduleRepository(FoMedDbContext dbContext)
    : RepositoryBase<DoctorSchedule>(dbContext), IDoctorScheduleRepository
{
    private readonly FoMedDbContext dbContext = dbContext;

    public async Task<IReadOnlyList<DoctorSchedule>> GetByDoctorIdAsync(
        int doctorId,
        CancellationToken cancellationToken = default) =>
        await dbContext.DoctorSchedules
            .AsNoTracking()
            .Where(schedule => schedule.DoctorId == doctorId && schedule.IsActive)
            .OrderBy(schedule => schedule.DayOfWeek)
            .ThenBy(schedule => schedule.StartTime)
            .ToListAsync(cancellationToken);

    public Task<DoctorSchedule?> GetByIdAndDoctorIdAsync(
        int scheduleId,
        int doctorId,
        CancellationToken cancellationToken = default) =>
        dbContext.DoctorSchedules
            .SingleOrDefaultAsync(
                schedule => schedule.Id == scheduleId && schedule.DoctorId == doctorId,
                cancellationToken);

    public Task<bool> HasOverlapAsync(
        int doctorId,
        byte dayOfWeek,
        TimeOnly startTime,
        TimeOnly endTime,
        int? excludedScheduleId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.DoctorSchedules.Where(schedule =>
            schedule.DoctorId == doctorId &&
            schedule.IsActive &&
            schedule.DayOfWeek == dayOfWeek &&
            schedule.StartTime < endTime &&
            schedule.EndTime > startTime);

        if (excludedScheduleId.HasValue)
        {
            query = query.Where(schedule => schedule.Id != excludedScheduleId.Value);
        }

        return query.AnyAsync(cancellationToken);
    }
}

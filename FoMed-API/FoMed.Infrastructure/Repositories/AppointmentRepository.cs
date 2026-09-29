using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IAppointmentRepository : IRepositoryBase<Appointment>
{
    Task<string> GenerateCodeAsync(CancellationToken cancellationToken = default);
    Task<Appointment?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsAsync(int doctorId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Appointment>> GetPatientAppointmentsAsync(int patientId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default);
    Task<bool> HasDoctorConflictAsync(int doctorId, DateTime startTime, DateTime endTime, int? excludeAppointmentId = null, CancellationToken cancellationToken = default);
    Task<bool> HasPatientConflictAsync(int patientId, DateTime startTime, DateTime endTime, int? excludeAppointmentId = null, CancellationToken cancellationToken = default);
    Task<bool> IsDoctorOnTimeOffAsync(int doctorId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default);
    Task<int> GetNextQueueNumberAsync(int doctorId, DateOnly date, CancellationToken cancellationToken = default);
    Task AddStatusHistoryAsync(AppointmentStatusHistory history, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppointmentStatusHistory>> GetStatusHistoryAsync(int appointmentId, CancellationToken cancellationToken = default);
}

public sealed class AppointmentRepository(FoMedDbContext dbContext)
    : RepositoryBase<Appointment>(dbContext), IAppointmentRepository
{
    private readonly FoMedDbContext dbContext = dbContext;

    public async Task<string> GenerateCodeAsync(CancellationToken cancellationToken = default)
    {
        var values = await dbContext.Database.SqlQueryRaw<int>(
            "SELECT NEXT VALUE FOR scheduling.seq_appointment_code AS Value").ToListAsync(cancellationToken);
        return "AP" + values.Single().ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task<Appointment?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialty)
            .Include(a => a.Service)
            .Include(a => a.AppointmentStatusHistories)
            .SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Appointment>> GetDoctorAppointmentsAsync(
        int doctorId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialty)
            .Include(a => a.Service)
            .Where(a => a.DoctorId == doctorId);

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = date.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(a => a.StartTime >= start && a.StartTime <= end);
        }
        if (status.HasValue)
        {
            var sb = (byte)status.Value;
            query = query.Where(a => a.Status == sb);
        }
        return await query.OrderBy(a => a.StartTime).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Appointment>> GetPatientAppointmentsAsync(
        int patientId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Include(a => a.Doctor).ThenInclude(d => d.Specialty)
            .Include(a => a.Service)
            .Where(a => a.PatientId == patientId);

        if (date.HasValue)
        {
            var start = date.Value.ToDateTime(TimeOnly.MinValue);
            var end = date.Value.ToDateTime(TimeOnly.MaxValue);
            query = query.Where(a => a.StartTime >= start && a.StartTime <= end);
        }
        if (status.HasValue)
        {
            var sb = (byte)status.Value;
            query = query.Where(a => a.Status == sb);
        }
        return await query.OrderByDescending(a => a.StartTime).ToListAsync(cancellationToken);
    }

    public Task<bool> HasDoctorConflictAsync(
        int doctorId, DateTime startTime, DateTime endTime, int? excludeAppointmentId = null, CancellationToken cancellationToken = default)
    {
        var cancelled = (byte)AppointmentStatus.Cancelled;
        var noShow = (byte)AppointmentStatus.NoShow;
        var query = dbContext.Appointments.Where(a =>
            a.DoctorId == doctorId && a.Status != cancelled && a.Status != noShow &&
            a.StartTime < endTime && a.EndTime > startTime);
        if (excludeAppointmentId.HasValue)
            query = query.Where(a => a.Id != excludeAppointmentId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> HasPatientConflictAsync(
        int patientId, DateTime startTime, DateTime endTime, int? excludeAppointmentId = null, CancellationToken cancellationToken = default)
    {
        var cancelled = (byte)AppointmentStatus.Cancelled;
        var noShow = (byte)AppointmentStatus.NoShow;
        var query = dbContext.Appointments.Where(a =>
            a.PatientId == patientId && a.Status != cancelled && a.Status != noShow &&
            a.StartTime < endTime && a.EndTime > startTime);
        if (excludeAppointmentId.HasValue)
            query = query.Where(a => a.Id != excludeAppointmentId.Value);
        return query.AnyAsync(cancellationToken);
    }

    public Task<bool> IsDoctorOnTimeOffAsync(
        int doctorId, DateTime startTime, DateTime endTime, CancellationToken cancellationToken = default) =>
        dbContext.DoctorTimeOffs.AnyAsync(to =>
            (to.DoctorId == null || to.DoctorId == doctorId) &&
            to.StartAt < endTime && to.EndAt > startTime, cancellationToken);

    public async Task<int> GetNextQueueNumberAsync(int doctorId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = date.ToDateTime(TimeOnly.MaxValue);
        var max = await dbContext.Appointments
            .Where(a => a.DoctorId == doctorId && a.StartTime >= start && a.StartTime <= end && a.QueueNumber.HasValue)
            .MaxAsync(a => (int?)a.QueueNumber, cancellationToken);
        return (max ?? 0) + 1;
    }

    public async Task AddStatusHistoryAsync(AppointmentStatusHistory history, CancellationToken cancellationToken = default) =>
        await dbContext.AppointmentStatusHistories.AddAsync(history, cancellationToken);

    public async Task<IReadOnlyList<AppointmentStatusHistory>> GetStatusHistoryAsync(
        int appointmentId, CancellationToken cancellationToken = default) =>
        await dbContext.AppointmentStatusHistories
            .AsNoTracking()
            .Include(h => h.ChangedByNavigation)
            .Where(h => h.AppointmentId == appointmentId)
            .OrderBy(h => h.ChangedAt)
            .ToListAsync(cancellationToken);
}

using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Repositories;

namespace FoMed.Infrastructure.UnitOfWork;

public interface IUnitOfWork
{
    Task<IWriteScope> BeginWriteAsync(CancellationToken cancellationToken = default);
    IUserRepository UserRepository { get; }
    IPatientRepository PatientRepository { get; }
    IDoctorRepository DoctorRepository { get; }
    ISpecialtyRepository SpecialtyRepository { get; }
    IDoctorScheduleRepository DoctorScheduleRepository { get; }
    IAppointmentRepository AppointmentRepository { get; }
    IServiceRepository ServiceRepository { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class UnitOfWork(
    FoMedDbContext dbContext,
    IUserRepository users,
    IPatientRepository patients,
    IDoctorRepository doctors,
    ISpecialtyRepository specialties,
    IAppointmentRepository appointments,
    IDoctorScheduleRepository doctorSchedules,
    IServiceRepository services) : IUnitOfWork
{
    public IUserRepository UserRepository { get; } = users;
    public IPatientRepository PatientRepository { get; } = patients;
    public IDoctorRepository DoctorRepository { get; } = doctors;
    public ISpecialtyRepository SpecialtyRepository { get; } = specialties;
    public IAppointmentRepository AppointmentRepository { get; } = appointments;
    public IDoctorScheduleRepository DoctorScheduleRepository { get; } = doctorSchedules;
    public IServiceRepository ServiceRepository { get; } = services;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public Task<IWriteScope> BeginWriteAsync(CancellationToken cancellationToken = default) =>
        WriteScope.BeginAsync(dbContext, cancellationToken);
}

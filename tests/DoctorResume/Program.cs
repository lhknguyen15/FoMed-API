using System.Reflection;
using FoMed.Api.Controllers;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

// Isolated in-memory fixtures only; no appsettings, local SQL or Azure connection.
var options = new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("DoctorResume-" + Guid.NewGuid()).Options;
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
var today = new DateTime(2026, 10, 6, 13, 30, 0);
await using (var db = new FoMedDbContext(options))
{
    var specialty = new Specialty { Id = 1, Name = "Demo", IsActive = true };
    var doctor = new Doctor { Id = 1, UserId = 11, FullName = "Doctor demo", IsActive = true, Specialty = specialty, User = new User { Id = 11, Username = "doctor-demo", PasswordHash = "not-a-credential", IsActive = true } };
    var other = new Doctor { Id = 2, UserId = 12, FullName = "Other doctor", IsActive = true, Specialty = specialty, User = new User { Id = 12, Username = "other-demo", PasswordHash = "not-a-credential", IsActive = true } };
    var patient = new Patient { Id = 1, PatientCode = "DEMO", FullName = "Patient demo", IsActive = true };
    Appointment Visit(int id, Doctor owner, AppointmentStatus status, DateTime at) => new() { Id = id, AppointmentCode = "AP-DEMO-" + id, Doctor = owner, Patient = patient, StartTime = at, EndTime = at.AddMinutes(30), Status = (byte)status, CheckedInAt = at.AddMinutes(-10) };
    MedicalRecord Record(int id, Appointment visit, bool finalized = false, Doctor? owner = null) => new() { Id = id, Appointment = visit, Doctor = owner ?? visit.Doctor, Patient = patient, IsFinalized = finalized, CreatedAt = visit.StartTime, Symptoms = "Saved draft demo" };
    db.AddRange(
        Record(101, Visit(1, doctor, AppointmentStatus.InProgress, today)),
        Record(102, Visit(2, doctor, AppointmentStatus.InProgress, today.AddDays(-1))),
        Record(103, Visit(3, other, AppointmentStatus.InProgress, today)),
        Record(104, Visit(4, doctor, AppointmentStatus.Completed, today), true),
        Record(105, Visit(5, doctor, AppointmentStatus.InProgress, today), true),
        Record(106, Visit(6, other, AppointmentStatus.InProgress, today), false, doctor),
        Visit(7, doctor, AppointmentStatus.Confirmed, today.AddHours(1)),
        Visit(8, doctor, AppointmentStatus.InProgress, today.AddHours(2)), // invalid missing record must not offer resume
        Record(109, Visit(9, doctor, AppointmentStatus.Cancelled, today)),
        new Doctor { Id = 3, UserId = 13, FullName = "Inactive doctor", IsActive = false, Specialty = specialty, User = new User { Id = 13, Username = "inactive-demo", PasswordHash = "not-a-credential", IsActive = true } });
    await db.SaveChangesAsync();
}

AppointmentService Service(FoMedDbContext db) => new(new UnitOfWork(db, new UserRepository(db), new PatientRepository(db), new DoctorRepository(db), new SpecialtyRepository(db), new AppointmentRepository(db), new DoctorScheduleRepository(db), new ServiceRepository(db)));

await using (var db = new FoMedDbContext(options))
{
    var service = Service(db);
    var result = await service.GetDoctorInProgressAsync(11);
    Check(result.StatusCode == 200, "Active doctor can list unfinished visits");
    Check(result.DataResponse!.Select(v => v.MedicalRecordId).SequenceEqual([102, 101]), "Own unfinished records, including earlier day, ordered deterministically");
    Check(result.DataResponse.All(v => v.Appointment.Status == AppointmentStatus.InProgress), "Resume list never mixes waiting/completed/canceled records");
    Check(result.DataResponse.All(v => v.Appointment.DoctorId == 1), "Foreign and inconsistent doctor ownership excluded");
    Check(result.DataResponse.Single(v => v.MedicalRecordId == 101).Appointment.Id == 1, "Resume exposes existing record ID, not appointment ID");
    Check(result.DataResponse.Single(v => v.MedicalRecordId == 101).StartedAt == today, "Start timestamp and appointment metadata mapped");
    var queue = await new AppointmentRepository(db).GetDoctorQueueAsync(1, DateOnly.FromDateTime(today));
    Check(queue.Count == 1 && queue[0].Id == 7, "Waiting queue unchanged and still Confirmed/check-in only");
    var other = await service.GetDoctorInProgressAsync(12);
    Check(other.DataResponse!.Count == 1 && other.DataResponse[0].MedicalRecordId == 103, "Second doctor sees only own draft");
    Check((await service.GetDoctorInProgressAsync(13)).StatusCode == 404, "Inactive doctor rejected");
    Check((await service.GetDoctorInProgressAsync(999)).StatusCode == 404, "User without doctor profile rejected");
    Check(await db.MedicalRecords.CountAsync() == 7 && await db.AppointmentStatusHistories.CountAsync() == 0, "Listing creates no record and no status transition");
}

// New DbContext simulates a fresh request/session; no navigation state or tracked entities survive.
await using (var db = new FoMedDbContext(options))
{
    var reopened = await Service(db).GetDoctorInProgressAsync(11);
    Check(reopened.DataResponse!.Any(v => v.MedicalRecordId == 101), "Existing draft remains available after fresh session");
    Check((await db.MedicalRecords.FindAsync(101))!.Symptoms == "Saved draft demo", "Resume listing preserves saved draft values");
    var record = (await db.MedicalRecords.FindAsync(101))!;
    record.IsFinalized = true;
    (await db.Appointments.FindAsync(1))!.Status = (byte)AppointmentStatus.Completed;
    await db.SaveChangesAsync();
}
await using (var db = new FoMedDbContext(options))
{
    var afterFinalize = await Service(db).GetDoctorInProgressAsync(11);
    Check(afterFinalize.DataResponse!.Count == 1 && afterFinalize.DataResponse[0].MedicalRecordId == 102, "Finalized visit disappears on subsequent request");
    (await db.MedicalRecords.FindAsync(102))!.IsFinalized = true;
    (await db.Appointments.FindAsync(2))!.Status = (byte)AppointmentStatus.Completed;
    await db.SaveChangesAsync();
    Check((await Service(db).GetDoctorInProgressAsync(11)).DataResponse!.Count == 0, "Empty list returned after all own visits finalized");
}
var action = typeof(AppointmentController).GetMethod(nameof(AppointmentController.GetDoctorInProgress))!;
Check(action.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Doctor", "Endpoint explicitly requires Doctor role");
Check(action.GetParameters().All(p => p.ParameterType == typeof(CancellationToken)), "Endpoint does not accept doctorId or client ownership override");
Console.WriteLine($"Doctor resume audit: {checks} passed. InMemory only; SQL translation and deployed HTTP not tested here.");

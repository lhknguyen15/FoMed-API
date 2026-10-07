using System.Reflection;
using FoMed.Api.Controllers;
using FoMed.Application.DTO.Appointment;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

// No configuration files or real database connections; fixtures and audit writes stay in RAM.
var options = new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("DoctorHistory-" + Guid.NewGuid()).Options;
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
var visitAt = new DateTime(2026, 10, 6, 13, 30, 0);
var expectedIds = new[] { 106, 105, 104, 103, 102 };
await using (var db = new FoMedDbContext(options))
{
    var role = new Role { Id = 1, Name = "Doctor" };
    var specialty = new Specialty { Id = 1, Name = "Demo", IsActive = true };
    Doctor Doctor(int id, bool active = true) => new() { Id = id, FullName = "Doctor DEMO " + id, IsActive = active, Specialty = specialty,
        User = new User { Id = id + 10, Username = "doctor-demo-" + id, FullName = "Doctor DEMO", PasswordHash = "not-a-credential", IsActive = true, UserRoles = [new FoMed.Infrastructure.Models.UserRole { Id = id, Role = role }] } };
    var owner = Doctor(1);
    var otherDoctor = Doctor(2);
    var inactive = Doctor(3, false);
    var patient = new Patient { Id = 1, PatientCode = "DEMO-1", FullName = "Patient DEMO 1", IsActive = true,
        User = new User { Id = 44, Username = "patient-demo", PasswordHash = "not-a-credential", IsActive = true } };
    var otherPatient = new Patient { Id = 2, PatientCode = "DEMO-2", FullName = "Patient DEMO 2", IsActive = true };
    Appointment Visit(int id, Patient person, Doctor doctor, AppointmentStatus status, DateTime at) => new() {
        Id = id, AppointmentCode = "AP-DEMO-" + id, Patient = person, Doctor = doctor, StartTime = at, EndTime = at.AddMinutes(30), Status = (byte)status, CheckedInAt = at.AddMinutes(-10) };
    MedicalRecord Record(int id, Appointment visit, bool finalized = true) => new() { Id = id, Appointment = visit, Patient = visit.Patient, Doctor = visit.Doctor,
        IsFinalized = finalized, Diagnosis = "Diagnosis DEMO " + id, Note = "Private clinical note DEMO " + id, CreatedAt = visitAt.AddMinutes(id) };
    db.Add(Record(1000, Visit(1000, patient, owner, AppointmentStatus.InProgress, visitAt), false));
    db.Add(Visit(1010, patient, owner, AppointmentStatus.Confirmed, visitAt));
    for (var i = 0; i < 7; i++)
        db.Add(Record(100 + i, Visit(100 + i, patient, i == 2 ? otherDoctor : owner, AppointmentStatus.Completed, visitAt.AddDays(-Math.Max(1, 6 - i)))));
    for (var i = 0; i < 30; i++)
        db.Add(Record(500 + i, Visit(500 + i, otherPatient, owner, AppointmentStatus.Completed, visitAt.AddDays(-1))));
    db.AddRange(
        Record(600, Visit(600, patient, owner, AppointmentStatus.Completed, visitAt.AddHours(-1)), false),
        Record(601, Visit(601, patient, owner, AppointmentStatus.InProgress, visitAt.AddHours(-1))),
        Record(602, Visit(602, patient, owner, AppointmentStatus.Cancelled, visitAt.AddHours(-1))),
        Record(603, Visit(603, patient, owner, AppointmentStatus.Completed, visitAt.AddDays(1))),
        Record(604, Visit(604, otherPatient, owner, AppointmentStatus.InProgress, visitAt.AddDays(1)), false),
        inactive);
    await db.SaveChangesAsync();
}
ClinicalService Clinical(FoMedDbContext db) { var repo = new ClinicRepository(db); return new(repo, new ClinicAccess(repo)); }
AppointmentService Appointments(FoMedDbContext db) => new(new UnitOfWork(db, new UserRepository(db), new PatientRepository(db), new DoctorRepository(db), new SpecialtyRepository(db), new AppointmentRepository(db), new DoctorScheduleRepository(db), new ServiceRepository(db)));
async Task Reject(ClinicalService service, int userId, int id, int status, string label)
{
    try { await service.GetRecordHistoryAsync(userId, id, default); throw new Exception("Unexpected access: " + label); }
    catch (ClinicException ex) { Check(ex.StatusCode == status, label); }
}
await using (var db = new FoMedDbContext(options))
{
    var service = Clinical(db);
    var history = await service.GetRecordHistoryAsync(11, 1000, default);
    Check(history.Select(h => h.MedicalRecordId).SequenceEqual(expectedIds), "Patient filter applied before top-five; old record beyond global page one included");
    Check(history.All(h => h.MedicalRecordId < 500), "No other patient history mixed in");
    Check(history.Any(h => h.MedicalRecordId == 102), "Prior finalized visit by another doctor included within assigned patient context");
    Check(history.Count == 5 && history[0].MedicalRecordId == 106 && history[1].MedicalRecordId == 105, "Five most recent visits with stable ID tie-break");
    Check(history[0].VisitAt == visitAt.AddDays(-1), "Visit date uses appointment, not record creation timestamp");
    Check(history.All(h => h.MedicalRecordId != 1000 && h.MedicalRecordId < 600), "Current, draft, non-completed, canceled and future visits excluded");
    var logs = await db.AuditLogs.OrderBy(l => l.Id).ToListAsync();
    Check(logs.Count == 5 && logs.Select(l => l.EntityId!.Value).SequenceEqual(expectedIds), "Read audit saved for exactly returned historical records");
    Check(logs.All(l => MedicalRecordAudit.ReadMetadata(l)?.Context.Source == "RecordHistory" && l.UserId == 11 && l.Action == "Read"), "Audit identifies source and doctor actor");
    Check(logs.All(l => !l.NewValue!.Contains("Diagnosis DEMO") && !l.NewValue.Contains("Private clinical note")), "No diagnosis or note copied into audit metadata");
    var countBefore = await db.AuditLogs.CountAsync();
    await Reject(service, 12, 1000, 403, "Unassigned doctor cannot read history through another doctor's current record");
    await Reject(service, 13, 1000, 403, "Inactive/unassigned doctor rejected");
    await Reject(service, 44, 1000, 403, "Patient cannot use doctor history service");
    await Reject(service, 999, 1000, 403, "Unknown user rejected");
    await Reject(service, 11, 99999, 404, "Missing current record rejected");
    Check(await db.AuditLogs.CountAsync() == countBefore, "Denied requests do not produce successful history-read logs");
    Check((await service.GetRecordHistoryAsync(11, 604, default)).Count == 5, "Second patient has their own history scope");
    var preview = await Appointments(db).GetDoctorQueueAsync(11, DateOnly.FromDateTime(visitAt));
    Check(preview.DataResponse!.Single().RecentHistory.Select(h => h.MedicalRecordId).SequenceEqual(expectedIds), "Queue preview and record page use identical history criteria");
    Check(await db.MedicalRecords.CountAsync() == 43 && (await db.MedicalRecords.FindAsync(1000))!.Diagnosis == "Diagnosis DEMO 1000", "History read does not mutate clinical records");

    // No-history is a successful empty result, not a fallback to a global record page.
    var emptyPatient = new Patient { Id = 3, PatientCode = "DEMO-3", FullName = "Patient DEMO empty", IsActive = true };
    var emptyVisit = new Appointment { Id = 605, AppointmentCode = "AP-DEMO-605", Patient = emptyPatient, DoctorId = 1,
        StartTime = visitAt, EndTime = visitAt.AddMinutes(30), Status = (byte)AppointmentStatus.InProgress };
    db.Add(new MedicalRecord { Id = 605, Patient = emptyPatient, Appointment = emptyVisit, DoctorId = 1, IsFinalized = false });
    await db.SaveChangesAsync();
    countBefore = await db.AuditLogs.CountAsync();
    Check((await service.GetRecordHistoryAsync(11, 605, default)).Count == 0, "Patient without prior finalized visits returns empty list");
    Check(await db.AuditLogs.CountAsync() == countBefore, "Empty history does not claim any historical record was read");
    emptyVisit.PatientId = 2;
    await db.SaveChangesAsync();
    await Reject(service, 11, 605, 403, "Inconsistent record/appointment patient context rejected");
    emptyVisit.PatientId = 3;
    emptyVisit.DoctorId = 2;
    await db.SaveChangesAsync();
    await Reject(service, 11, 605, 403, "Inconsistent record/appointment doctor context rejected");
}
await using (var db = new FoMedDbContext(options))
{
    Check((await Clinical(db).GetRecordHistoryAsync(11, 1000, default)).Select(h => h.MedicalRecordId).SequenceEqual(expectedIds), "Fresh request/session returns identical results without navigation state");
    (await db.Users.FindAsync(11))!.IsActive = false;
    await db.SaveChangesAsync();
    await Reject(Clinical(db), 11, 1000, 403, "Inactive user rejected even if doctor profile active");
    (await db.Users.FindAsync(11))!.IsActive = true;
    var role = await db.UserRoles.SingleAsync(ur => ur.UserId == 11);
    db.UserRoles.Remove(role);
    await db.SaveChangesAsync();
    await Reject(Clinical(db), 11, 1000, 403, "Removed Doctor role rejected at service level");
}
// The real shared LINQ must translate on SQL Server, without opening a connection.
var sqlOptions = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=DoctorHistoryQueryOnly;Integrated Security=true;Encrypt=true;TrustServerCertificate=false").Options;
await using (var db = new FoMedDbContext(sqlOptions))
{
    var sql = MedicalRecordHistoryQuery.Recent(db.MedicalRecords.AsNoTracking(), 1, 1000, visitAt)
        .Select(r => new PatientHistorySummary(r.Id, r.AppointmentId, r.Appointment.StartTime, r.Diagnosis, r.Note)).ToQueryString();
    Check(sql.Contains("TOP(") && sql.Contains("patient_id") && sql.Contains("ORDER BY"), "Production SQL Server query translates with patient filter/top/order; no connection opened");
}
Check(typeof(ClinicalController).GetMethod(nameof(ClinicalController.RecordHistory))!.GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Doctor", "History HTTP action requires Doctor role");
Console.WriteLine($"Doctor history audit: {checks} passed. InMemory + SQL translation only, not deployed HTTP.");

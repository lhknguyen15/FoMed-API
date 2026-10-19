using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Doctor;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

// Real SQL transactions/application locks, fake records, no application configuration.
// This runner cannot target Azure, FoMedDb or any existing application database.
var passed = 0;
var failed = 0;
void Check(bool ok, string name)
{
    if (ok) passed++; else failed++;
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {name}");
}
async Task Reject(int status, Func<Task> action, string name, string? expectedMessage = null)
{
    try { await action(); Check(false, name + " (request unexpectedly accepted)"); }
    catch (ClinicException error) { Check(error.StatusCode == status && (expectedMessage is null || error.Message == expectedMessage), name + $" (status {error.StatusCode})"); }
    catch (DbUpdateException error) when (error.InnerException is SqlException sql)
    {
        Check(false, name + $" (unexpected DbUpdateException, SQL {sql.Number})");
    }
    catch (Exception error) { Check(false, name + $" (unexpected {error.GetType().Name})"); }
}
User Actor(string name, Role role) => new()
{
    Username = name, FullName = "Account DEMO", IsActive = true,
    PasswordHash = "synthetic-not-a-password", UserRoles = [new UserRole { Role = role }]
};
var name = "FoMed_Audit_" + Guid.NewGuid().ToString("N");
var connection = LocalAuditDatabase.Master(name);
connection.InitialCatalog = name;
LocalAuditDatabase.Guard(connection, name);
var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(connection.ConnectionString).Options;
async Task<T> Fresh<T>(Func<DoctorTimeOffService, Task<T>> action)
{
    await using var fresh = new FoMedDbContext(options);
    var repository = new ClinicRepository(fresh);
    return await action(new DoctorTimeOffService(repository, new ClinicAccess(repository)));
}
async Task FreshVoid(Func<DoctorTimeOffService, Task> action)
{
    await using var fresh = new FoMedDbContext(options);
    var repository = new ClinicRepository(fresh);
    await action(new DoctorTimeOffService(repository, new ClinicAccess(repository)));
}
var start = new DateTime(2030, 1, 3, 8, 0, 0, DateTimeKind.Unspecified);
SaveDoctorTimeOffRequest Input(int? doctorId, DateTime? from = null, DateTime? to = null, string? reason = "DEMO") =>
    new() { DoctorId = doctorId, StartAt = from ?? start, EndAt = to ?? start.AddHours(4), Reason = reason };
try
{
    await using var db = new FoMedDbContext(options);
    await db.Database.EnsureCreatedAsync();
    var doctorRole = new Role { Name = "Doctor" };
    var specialty = new Specialty { Name = "DEMO", IsActive = true };
    var admin = Actor("admin-audit", new Role { Name = "Admin" });
    var patientUser = Actor("patient-audit", new Role { Name = "Patient" });
    var doctor = new Doctor { FullName = "Doctor A DEMO", IsActive = true, User = Actor("doctor-a-audit", doctorRole), Specialty = specialty };
    var other = new Doctor { FullName = "Doctor B DEMO", IsActive = true, User = Actor("doctor-b-audit", doctorRole), Specialty = specialty };
    var inactive = new Doctor { FullName = "Inactive doctor DEMO", IsActive = false, User = Actor("inactive-doctor-audit", doctorRole), Specialty = specialty };
    db.AddRange(admin, patientUser, doctor, other, inactive);
    await db.SaveChangesAsync();

    await Reject(403, () => Fresh(s => s.CreateAsync(patientUser.Id, Input(doctor.Id), true, default)), "BACK-01 Patient cannot create admin leave");
    var clinic = await Fresh(s => s.CreateAsync(admin.Id, Input(null), true, default));
    Check(clinic.DoctorId is null, "BACK-02 Admin can create clinic-wide leave");
    var individual = await Fresh(s => s.CreateAsync(admin.Id, Input(doctor.Id, start.AddDays(1), start.AddDays(1).AddHours(4), "  DEMO  "), true, default));
    Check(individual.DoctorId == doctor.Id && individual.Reason == "DEMO", "BACK-03 Admin can create doctor leave and trim reason");
    Check((await Fresh(s => s.ListAsync(admin.Id, true, null, default))).Count == 2, "BACK-04 Admin lists both leave scopes");
    await Reject(400, () => Fresh(s => s.CreateAsync(admin.Id, Input(null, to: start), true, default)), "BACK-05 End must be after start");
    await Reject(403, () => FreshVoid(s => s.DeleteAsync(patientUser.Id, individual.Id, true, default)), "BACK-06 Patient cannot delete through admin path");

    var patient = new Patient { PatientCode = "BN-ADMIN-DEMO", FullName = "Patient DEMO", IsActive = true };
    var visit = new Appointment { AppointmentCode = "AP-ADMIN-DEMO-A", DoctorId = doctor.Id, StartTime = start.AddDays(2), EndTime = start.AddDays(2).AddMinutes(30), Status = 1, Patient = patient };
    var otherVisit = new Appointment { AppointmentCode = "AP-ADMIN-DEMO-B", DoctorId = other.Id, StartTime = start.AddDays(4), EndTime = start.AddDays(4).AddMinutes(30), Status = 1, Patient = patient };
    db.AddRange(visit, otherVisit);
    await db.SaveChangesAsync();
    await Reject(409, () => Fresh(s => s.CreateAsync(admin.Id, Input(doctor.Id, visit.StartTime, visit.EndTime), true, default)), "BACK-07 Booked range rejects doctor leave");
    await Reject(409, () => Fresh(s => s.CreateAsync(admin.Id, Input(null, visit.StartTime, visit.EndTime), true, default)), "BACK-08 Booked range rejects clinic-wide leave");
    await Reject(409, () => Fresh(s => s.UpdateAsync(admin.Id, clinic.Id, Input(null, visit.StartTime, visit.EndTime), true, default)), "BACK-09 Update cannot cover an active appointment");
    Check((await Fresh(s => s.ListAsync(admin.Id, true, null, default))).Single(item => item.Id == clinic.Id).StartAt == clinic.StartAt, "BACK-10 Rejected update keeps original interval");
    await FreshVoid(s => s.DeleteAsync(admin.Id, clinic.Id, true, default));
    Check(!(await Fresh(s => s.ListAsync(admin.Id, true, null, default))).Any(item => item.Id == clinic.Id), "BACK-11 Delete removes exact selected leave");
    await Reject(404, () => FreshVoid(s => s.DeleteAsync(admin.Id, clinic.Id, true, default)), "BACK-12 Missing record deletion does not report success");
    var changed = await Fresh(s => s.UpdateAsync(admin.Id, individual.Id, Input(null, individual.StartAt, individual.EndAt), true, default));
    Check(changed.DoctorId is null, "BACK-13 Admin changing doctor leave to clinic-wide must clear DoctorId");
    Check((await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == individual.Id)).DoctorId is null, "BACK-26 Clinic-wide scope is persisted, not only returned");
    var conflictSource = await Fresh(s => s.CreateAsync(admin.Id, Input(doctor.Id, start.AddDays(5), start.AddDays(5).AddHours(1), "Original reason"), true, default));
    var auditBeforeConflict = await db.AuditLogs.CountAsync();
    await Reject(409, () => Fresh(s => s.UpdateAsync(admin.Id, conflictSource.Id, Input(null, otherVisit.StartTime, otherVisit.EndTime, "Rejected change"), true, default)), "BACK-14 Scope expansion must detect another doctor's appointment");
    var afterConflict = await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == conflictSource.Id);
    Check(afterConflict.DoctorId == conflictSource.DoctorId && afterConflict.StartAt == conflictSource.StartAt && afterConflict.EndAt == conflictSource.EndAt && afterConflict.Reason == conflictSource.Reason, "BACK-27 Rejected scope change preserves doctor, interval and reason");
    Check(await db.AuditLogs.CountAsync() == auditBeforeConflict, "BACK-28 Rejected scope change does not record a successful update");
    var narrowed = await Fresh(s => s.UpdateAsync(admin.Id, individual.Id, Input(doctor.Id, individual.StartAt, individual.EndAt), true, default));
    Check(narrowed.DoctorId == doctor.Id && (await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == individual.Id)).DoctorId == doctor.Id, "BACK-29 Clinic-wide leave can narrow to one doctor");
    var reassigned = await Fresh(s => s.UpdateAsync(admin.Id, individual.Id, Input(other.Id, individual.StartAt, individual.EndAt), true, default));
    Check(reassigned.DoctorId == other.Id && (await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == individual.Id)).DoctorId == other.Id, "BACK-30 Admin can reassign leave to another doctor");
    var omittedPayload = JsonSerializer.Serialize(new { startAt = individual.StartAt, endAt = individual.EndAt, reason = "Omitted doctor scope" });
    var omittedInput = JsonSerializer.Deserialize<SaveDoctorTimeOffRequest>(omittedPayload, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    var omittedResult = await Fresh(s => s.UpdateAsync(admin.Id, individual.Id, omittedInput, true, default));
    Check(omittedResult.DoctorId is null && (await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == individual.Id)).DoctorId is null, "BACK-32 Omitted admin doctorId selects clinic-wide scope like explicit null");

    await Reject(403, () => Fresh(s => s.CreateAsync(doctor.UserId, Input(other.Id, start.AddDays(6), start.AddDays(6).AddHours(1)), false, default)), "BACK-15 Doctor cannot create leave for another doctor");
    await Reject(403, () => FreshVoid(s => s.DeleteAsync(other.UserId, conflictSource.Id, false, default)), "BACK-16 Doctor cannot delete another doctor's leave");
    var own = await Fresh(s => s.CreateAsync(doctor.UserId, Input(null, start.AddDays(7), start.AddDays(7).AddHours(1)), false, default));
    Check(own.DoctorId == doctor.Id, "BACK-17 Own-doctor path resolves doctor without accepting clinic-wide scope");
    var ownUpdate = await Fresh(s => s.UpdateAsync(doctor.UserId, own.Id, Input(null, own.StartAt, own.EndAt, "Own doctor update"), false, default));
    Check(ownUpdate.DoctorId == doctor.Id && (await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == own.Id)).DoctorId == doctor.Id, "BACK-31 Doctor update with omitted scope remains doctor-specific");
    var clinicAgain = await Fresh(s => s.CreateAsync(admin.Id, Input(null, start.AddDays(8), start.AddDays(8).AddHours(1)), true, default));
    await Reject(403, () => FreshVoid(s => s.DeleteAsync(doctor.UserId, clinicAgain.Id, false, default)), "BACK-18 Doctor cannot delete clinic-wide leave");
    Check((await Fresh(s => s.ListAsync(doctor.UserId, false, other.Id, default))).All(item => item.DoctorId == doctor.Id), "BACK-19 Doctor list cannot read another doctor's leave through filter");
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, int.MaxValue, Input(null), true, default)), "BACK-20 Missing record update returns not found");
    var boundary = await Fresh(s => s.CreateAsync(admin.Id, Input(doctor.Id, visit.EndTime, visit.EndTime.AddMinutes(30)), true, default));
    Check(boundary.StartAt == visit.EndTime, "BACK-21 Adjacent non-overlapping interval is allowed");
    var leavesBeforeInvalidDoctor = await db.DoctorTimeOffs.CountAsync();
    var auditsBeforeInvalidDoctor = await db.AuditLogs.CountAsync();
    const string unavailableDoctorMessage = "Không tìm thấy bác sĩ đang hoạt động.";
    await Reject(404, () => Fresh(s => s.CreateAsync(admin.Id, Input(int.MaxValue, start.AddDays(9), start.AddDays(9).AddHours(1)), true, default)), "BACK-22 Unknown doctor returns a clear domain error", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.CreateAsync(admin.Id, Input(inactive.Id, start.AddDays(10), start.AddDays(10).AddHours(1)), true, default)), "BACK-23 Inactive doctor cannot receive new leave", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.CreateAsync(admin.Id, Input(0), true, default)), "BACK-33 Zero doctor ID is not clinic-wide scope", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.CreateAsync(admin.Id, Input(-1), true, default)), "BACK-34 Negative doctor ID is rejected", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, conflictSource.Id, Input(int.MaxValue, start.AddDays(11), start.AddDays(11).AddHours(1), "Invalid doctor update"), true, default)), "BACK-35 Update cannot assign a nonexistent doctor", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, conflictSource.Id, Input(inactive.Id, start.AddDays(11), start.AddDays(11).AddHours(1), "Inactive doctor update"), true, default)), "BACK-36 Update cannot assign an inactive doctor", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, conflictSource.Id, Input(0), true, default)), "BACK-37 Update rejects zero doctor ID", unavailableDoctorMessage);
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, conflictSource.Id, Input(-1), true, default)), "BACK-38 Update rejects negative doctor ID", unavailableDoctorMessage);
    var afterInvalidDoctor = await db.DoctorTimeOffs.AsNoTracking().SingleAsync(t => t.Id == conflictSource.Id);
    Check(afterInvalidDoctor.DoctorId == conflictSource.DoctorId && afterInvalidDoctor.StartAt == conflictSource.StartAt && afterInvalidDoctor.EndAt == conflictSource.EndAt && afterInvalidDoctor.Reason == conflictSource.Reason, "BACK-39 Invalid doctor updates preserve all original leave fields");
    Check(await db.AuditLogs.CountAsync() == auditsBeforeInvalidDoctor, "BACK-40 Rejected doctor requests do not add success audits");
    Check(await db.DoctorTimeOffs.CountAsync() == leavesBeforeInvalidDoctor, "BACK-41 Rejected doctor requests add no leave records");
    Check(await db.AuditLogs.AsNoTracking().AnyAsync(a => a.Entity == "DoctorTimeOff" && a.Action == "Delete" && a.EntityId == clinic.Id && a.UserId == admin.Id), "BACK-24 Successful deletion is audited with actor and record ID");
    Check(!await db.DoctorTimeOffs.AsNoTracking().AnyAsync(t => t.DoctorId == int.MaxValue), "BACK-25 Invalid doctor request leaves no invalid leave record");

    // Existing history remains readable/deletable after its doctor is deactivated.
    var history = new DoctorTimeOff { DoctorId = inactive.Id, StartAt = start.AddDays(-30), EndAt = start.AddDays(-30).AddHours(1), Reason = "Existing history" };
    var convertibleHistory = new DoctorTimeOff { DoctorId = inactive.Id, StartAt = start.AddDays(-29), EndAt = start.AddDays(-29).AddHours(1), Reason = "Existing history to reassign" };
    db.AddRange(history, convertibleHistory);
    await db.SaveChangesAsync();
    Check((await Fresh(s => s.ListAsync(admin.Id, true, inactive.Id, default))).Any(t => t.Id == history.Id), "BACK-42 Admin can still read inactive doctor's leave history");
    await Reject(404, () => Fresh(s => s.UpdateAsync(admin.Id, history.Id, Input(inactive.Id, history.StartAt, history.EndAt), true, default)), "BACK-43 Updating leave assigned to an inactive doctor requires an available scope", unavailableDoctorMessage);
    await FreshVoid(s => s.DeleteAsync(admin.Id, history.Id, true, default));
    Check(!await db.DoctorTimeOffs.AsNoTracking().AnyAsync(t => t.Id == history.Id), "BACK-44 Admin can delete inactive doctor's existing leave");
    var movedHistory = await Fresh(s => s.UpdateAsync(admin.Id, convertibleHistory.Id, Input(null, convertibleHistory.StartAt, convertibleHistory.EndAt), true, default));
    Check(movedHistory.DoctorId is null, "BACK-45 Inactive doctor's existing leave can move to clinic-wide scope");
}
finally
{
    LocalAuditDatabase.Guard(connection, name);
    await using var cleanup = new FoMedDbContext(options);
    await cleanup.Database.EnsureDeletedAsync();
    Console.WriteLine("Removed isolated localhost database: " + name);
}
Console.WriteLine($"Admin schedule backend assessment: {passed} passed, {failed} failed. No cloud writes.");
Environment.ExitCode = failed == 0 ? 0 : 1;

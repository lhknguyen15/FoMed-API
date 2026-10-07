using System.Reflection;
using FoMed.Api.Controllers.Admin;
using FoMed.Api.Controllers.Reception;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.DTO.Appointment;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Doctor;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Synthetic data only. Never read private appsettings or connect to Azure.
if (args.Any(a => a != "--sql")) throw new ArgumentException("Only --sql is supported.");
int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
async Task Expect(int status, Func<Task> action, string name)
{
    try { await action(); throw new Exception("Expected rejection: " + name); }
    catch (ClinicException e) { Check(e.StatusCode == status, name); }
}
AdminDoctorScheduleService Service(FoMedDbContext db) { var r = new ClinicRepository(db); return new(r, new ClinicAccess(r)); }
UnitOfWork Work(FoMedDbContext db) => new(db, new UserRepository(db), new PatientRepository(db), new DoctorRepository(db), new SpecialtyRepository(db), new AppointmentRepository(db), new DoctorScheduleRepository(db), new ServiceRepository(db));
DoctorScheduleService OwnService(FoMedDbContext db) => new(Work(db), new ClinicRepository(db));
User Actor(string name, string role, bool active = true) => new() { Username = name, FullName = "Account DEMO", PasswordHash = "not-a-password", IsActive = active, UserRoles = [new UserRole { Role = new Role { Name = role } }] };
SaveAdminDoctorScheduleRequest Input(DoctorSchedule s, TimeOnly? end = null, int? slot = null, bool? active = null, string? version = null) => new() { DoctorId = s.DoctorId, DayOfWeek = s.DayOfWeek, StartTime = s.StartTime, EndTime = end ?? s.EndTime, SlotMinutes = slot ?? s.SlotMinutes, IsActive = active ?? s.IsActive, ExpectedVersion = version };

Check(typeof(AdminDoctorScheduleController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin", "Existing Admin endpoint stays Admin only");
Check(typeof(ReceptionDoctorScheduleController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Receptionist,Admin", "Reception controller admits only scheduling staff");
Check(typeof(ReceptionDoctorScheduleController).GetMethods().Where(m => m.Name.Contains("TimeOff")).All(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpGetAttribute")), "Reception leave endpoint is read only");
var date = DateOnly.FromDateTime(ClinicTime.Now).AddDays(7).ToDateTime(new TimeOnly(9, 0));
var shift = new DoctorSchedule { DoctorId = 10, DayOfWeek = (byte)date.DayOfWeek, StartTime = new(8, 0), EndTime = new(12, 0), SlotMinutes = 30, IsActive = true };
var visit = new Appointment { DoctorId = 10, StartTime = date, EndTime = date.AddMinutes(30) };
Check(DoctorScheduleSafety.Covers(shift, visit), "Matching slot covered");
foreach (var invalid in new[] {
    new DoctorSchedule { DoctorId = 11, DayOfWeek = shift.DayOfWeek, StartTime = shift.StartTime, EndTime = shift.EndTime, SlotMinutes = 30, IsActive = true },
    new DoctorSchedule { DoctorId = 10, DayOfWeek = shift.DayOfWeek, StartTime = new(8, 10), EndTime = shift.EndTime, SlotMinutes = 30, IsActive = true },
    new DoctorSchedule { DoctorId = 10, DayOfWeek = shift.DayOfWeek, StartTime = shift.StartTime, EndTime = shift.EndTime, SlotMinutes = 60, IsActive = true },
    new DoctorSchedule { DoctorId = 10, DayOfWeek = shift.DayOfWeek, StartTime = shift.StartTime, EndTime = shift.EndTime, SlotMinutes = 30, IsActive = false } })
    Check(!DoctorScheduleSafety.Covers(invalid, visit), "Wrong doctor/grid/duration/inactive shift not coverage");

var memory = new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("ReceptionSchedules-" + Guid.NewGuid()).Options;
await using (var db = new FoMedDbContext(memory))
{
    var receptionist = Actor("r", "Receptionist"); var patient = Actor("p", "Patient");
    var doctor = new Doctor { FullName = "Bác sĩ DEMO", IsActive = true, User = Actor("d", "Doctor"), Specialty = new Specialty { Name = "DEMO", IsActive = true } };
    db.AddRange(receptionist, patient, doctor); await db.SaveChangesAsync();
    db.AddRange(new DoctorSchedule { DoctorId = doctor.Id, DayOfWeek = (byte)(((int)ClinicTime.Now.DayOfWeek + 1) % 7), StartTime = new(8, 0), EndTime = new(12, 0), SlotMinutes = 30, IsActive = true },
        new DoctorTimeOff { DoctorId = doctor.Id, StartAt = date, EndAt = date.AddHours(1), Reason = "DEMO" },
        new DoctorTimeOff { DoctorId = null, StartAt = date.AddDays(1), EndAt = date.AddDays(1).AddHours(1), Reason = "Toàn phòng khám DEMO" });
    await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    var service = Service(db);
    Check((await service.ListAsync(receptionist.Id, doctor.Id, default, true)).Single().Version.Length == 64, "Reception reads real shifts with edit version");
    Check((await service.ReceptionDoctorsAsync(receptionist.Id, default)).Single().DoctorId == doctor.Id, "Narrow doctor choice query");
    Check((await service.ReceptionTimeOffAsync(receptionist.Id, doctor.Id, default)).Count == 2, "Doctor filter includes clinic-wide leave");
    await Expect(403, () => service.ListAsync(receptionist.Id, null, default), "Reception cannot call Admin service path");
    await Expect(403, () => service.ListAsync(patient.Id, null, default, true), "Patient cannot read staff schedules");
    await Expect(403, () => service.ReceptionDoctorsAsync(99999, default), "Unknown user rejected");
    Check(db.ChangeTracker.Entries().Count() == 0, "Scheduling reads do not write or track entities");
    var todayAtNine = ClinicTime.Now.Date.AddHours(9);
    var todayShift = new DoctorSchedule { DoctorId = doctor.Id, DayOfWeek = (byte)todayAtNine.DayOfWeek, StartTime = new(8, 0), EndTime = new(12, 0), SlotMinutes = 30, IsActive = true };
    var todayVisit = new Appointment { AppointmentCode = "AP-TODAY-DEMO", DoctorId = doctor.Id, StartTime = todayAtNine, EndTime = todayAtNine.AddMinutes(30), Status = 0, Patient = new Patient { PatientCode = "BN-TODAY-DEMO", FullName = "Bệnh nhân DEMO", IsActive = true } };
    db.AddRange(todayShift, todayVisit); await db.SaveChangesAsync();
    await Expect(409, () => DoctorScheduleSafety.EnsureAsync(new ClinicRepository(db), todayShift, new DoctorSchedule(), default), "Today's pending visit protected even after scheduled time passes");
    todayVisit.StartTime = todayAtNine.AddDays(-7); todayVisit.EndTime = todayVisit.StartTime.AddMinutes(30); await db.SaveChangesAsync();
    await DoctorScheduleSafety.EnsureAsync(new ClinicRepository(db), todayShift, new DoctorSchedule(), default);
    Check(true, "Expired pending visit from prior day does not lock weekly configuration forever");
}

if (args.Contains("--sql"))
{
    var name = "FoMed_Schedules_Test_" + Guid.NewGuid().ToString("N");
    var connection = new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = name, IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 5 };
    if (connection.DataSource != "localhost" || connection.InitialCatalog != name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^FoMed_Schedules_Test_[a-f0-9]{32}$")) throw new Exception("Unsafe test DB target.");
    var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(connection.ConnectionString).Options;
    try
    {
        await using var db = new FoMedDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var r = Actor("reception-demo", "Receptionist"); var admin = Actor("admin-demo", "Admin"); var bad = Actor("patient-demo", "Patient");
        var inactive = new User { Username = "inactive-demo", FullName = "Inactive DEMO", PasswordHash = "not-a-password", IsActive = false, UserRoles = [new UserRole { Role = r.UserRoles.Single().Role }] };
        var doctor = new Doctor { FullName = "Bác sĩ DEMO", IsActive = true, User = Actor("doctor-demo", "Doctor"), Specialty = new Specialty { Name = "DEMO", IsActive = true } };
        db.AddRange(r, admin, bad, inactive, doctor); await db.SaveChangesAsync();
        var rId = r.Id; var adminId = admin.Id; var doctorId = doctor.Id; var doctorUser = doctor.UserId;
        async Task<T> Fresh<T>(Func<AdminDoctorScheduleService, Task<T>> action) { await using var fresh = new FoMedDbContext(options); return await action(Service(fresh)); }
        async Task FreshVoid(Func<AdminDoctorScheduleService, Task> action) { await using var fresh = new FoMedDbContext(options); await action(Service(fresh)); }
        var batch = new SaveAdminDoctorScheduleBatchRequest { DoctorId = doctorId, DayOfWeeks = [1, 2, 3, 4, 5, 6, 0], StartTime = new(8, 0), EndTime = new(12, 0), SlotMinutes = 30, IsActive = true };
        foreach (var actor in new[] { bad.Id, inactive.Id, 99999 })
            await Expect(403, () => Fresh(s => s.CreateBatchAsync(actor, batch, default, true)), "Write rejects non-staff/inactive/missing actor");
        await Expect(403, () => Fresh(s => s.CreateBatchAsync(rId, batch, default)), "Reception cannot create through Admin path");
        foreach (var invalid in new[] { batch with { DayOfWeeks = [] }, batch with { DayOfWeeks = [1, 1] }, batch with { DayOfWeeks = [7] }, batch with { SlotMinutes = 4 }, batch with { SlotMinutes = 241 }, batch with { StartTime = new(8, 0, 1) }, batch with { EndTime = new(8, 10), SlotMinutes = 30 }, batch with { EndTime = batch.StartTime }, batch with { DoctorId = 99999 } })
            await Expect(invalid.DoctorId == 99999 ? 404 : 400, () => Fresh(s => s.CreateBatchAsync(rId, invalid, default, true)), "Invalid batch leaves no partial schedule");
        Check(!await db.DoctorSchedules.AnyAsync(), "All invalid requests leave table empty");
        var created = await Fresh(s => s.CreateBatchAsync(rId, batch, default, true));
        Check(created.Count == 7 && created.All(s => s.Version.Length == 64), "Reception creates all seven days atomically with versions");
        Check(await db.AuditLogs.CountAsync(a => a.Entity == "DoctorSchedule" && a.EntityId != null) == 7, "Every created shift has audit ID");
        await Expect(409, () => Fresh(s => s.CreateBatchAsync(rId, batch, default, true)), "Overlapping shift rejected");
        var mixedBatch = batch with { DayOfWeeks = [0, 1], StartTime = new(11, 0), EndTime = new(13, 0) };
        await Expect(409, () => Fresh(s => s.CreateBatchAsync(rId, mixedBatch, default, true)), "Overlapping batch rejected as one transaction");
        Check(await db.DoctorSchedules.CountAsync() == 7, "Rejected batch adds no days");
        var target = created.Single(s => s.DayOfWeek == (byte)date.DayOfWeek);
        var slots = await new AppointmentService(Work(db)).GetAvailableSlotsAsync(doctorId, DateOnly.FromDateTime(date));
        Check(slots.DataResponse?.Count == 8 && slots.DataResponse.All(s => s.IsAvailable), "New staff shifts immediately appear in booking slots");
        var timeOff = new DoctorTimeOff { DoctorId = null, StartAt = date, EndAt = date.AddHours(1), Reason = "DEMO" };
        db.Add(timeOff); await db.SaveChangesAsync();
        slots = await new AppointmentService(Work(db)).GetAvailableSlotsAsync(doctorId, DateOnly.FromDateTime(date));
        Check(slots.DataResponse?.Count(s => !s.IsAvailable) == 2, "Clinic-wide leave overrides recurring work shift during booking");
        db.Remove(timeOff); await db.SaveChangesAsync();
        var original = new DoctorSchedule { Id = target.Id, DoctorId = doctorId, DayOfWeek = target.DayOfWeek, StartTime = target.StartTime, EndTime = target.EndTime, SlotMinutes = target.SlotMinutes, IsActive = true };
        var demoPatient = new Patient { PatientCode = "BN-DEMO", FullName = "Bệnh nhân DEMO", IsActive = true };
        db.Add(demoPatient); await db.SaveChangesAsync();
        var appointment = new Appointment { AppointmentCode = "AP-DEMO-1", DoctorId = doctorId, PatientId = demoPatient.Id, StartTime = date, EndTime = date.AddMinutes(30), Status = 1, CreatedAt = DateTime.UtcNow };
        db.Add(appointment); await db.SaveChangesAsync();
        await Expect(400, () => Fresh(s => s.UpdateAsync(rId, target.Id, Input(original), default, true)), "Reception edit requires loaded version");
        await Expect(409, () => Fresh(s => s.UpdateAsync(rId, target.Id, Input(original, version: "stale"), default, true)), "Stale edit rejected");
        await Expect(409, () => Fresh(s => s.UpdateAsync(rId, target.Id, Input(original, end: new(9, 0), version: target.Version), default, true)), "Shrinking shift cannot strand booked visit");
        await Expect(409, () => Fresh(s => s.UpdateAsync(rId, target.Id, Input(original, slot: 60, version: target.Version), default, true)), "Duration change cannot reinterpret existing booking");
        await Expect(409, () => Fresh(s => s.UpdateAsync(rId, target.Id, Input(original, active: false, version: target.Version), default, true)), "Edit-to-inactive guard");
        await Expect(409, () => FreshVoid(s => s.DeleteAsync(rId, target.Id, default, true, target.Version)), "Deactivate rejects booked shift");
        await Expect(409, () => FreshVoid(s => s.DeleteAsync(adminId, target.Id, default)), "Admin cannot bypass booked shift guard");
        await using (var fresh = new FoMedDbContext(options))
            await Expect(409, () => OwnService(fresh).DeleteAsync(doctorUser, target.Id, default), "Own-doctor delete also protects bookings");
        await using (var fresh = new FoMedDbContext(options))
            await Expect(409, () => OwnService(fresh).UpdateAsync(doctorUser, target.Id, new SaveDoctorScheduleRequest { DayOfWeek = target.DayOfWeek, StartTime = new(8, 0), EndTime = new(9, 0), SlotMinutes = 30 }, default), "Own-doctor update also protects bookings");
        var expanded = await Fresh(s => s.UpdateAsync(rId, target.Id, Input(original, end: new(13, 0), version: target.Version), default, true));
        Check(expanded.EndTime == new TimeOnly(13, 0) && expanded.Version != target.Version, "Safe extension allowed and version changes");
        Check((await db.Appointments.AsNoTracking().SingleAsync()).StartTime == date, "Schedule update never moves appointment");
        var updates = await db.AuditLogs.Where(a => a.Action == "Update" && a.EntityId == target.Id).AsNoTracking().ToListAsync();
        Check(updates.Single().OldValue != null && updates.Single().NewValue != null, "Updates audit before and after values");
        // Noncompleted past visits are protected; completed/cancelled records do not prevent changes.
        appointment.StartTime = date.AddDays(-14); appointment.EndTime = appointment.StartTime.AddMinutes(30); appointment.Status = 2; await db.SaveChangesAsync();
        await Expect(409, () => FreshVoid(s => s.DeleteAsync(rId, target.Id, default, true, expanded.Version)), "Unfinished past visit still protects shift");
        appointment.Status = 3; await db.SaveChangesAsync();
        await FreshVoid(s => s.DeleteAsync(rId, target.Id, default, true, expanded.Version));
        Check(!(await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == target.Id)).IsActive && await db.Appointments.CountAsync() == 1, "Completed history retained after soft deactivation");
        // Race on separate SQL connections: one stale writer must lose.
        var other = created.First(s => s.Id != target.Id);
        var otherEntity = new DoctorSchedule { Id = other.Id, DoctorId = doctorId, DayOfWeek = other.DayOfWeek, StartTime = other.StartTime, EndTime = other.EndTime, SlotMinutes = 30, IsActive = true };
        async Task<int> Race(TimeOnly end)
        {
            try { await Fresh(s => s.UpdateAsync(rId, other.Id, Input(otherEntity, end: end, version: other.Version), default, true)); return 200; }
            catch (ClinicException e) { return e.StatusCode; }
        }
        var results = await Task.WhenAll(Race(new(13, 0)), Race(new(14, 0)));
        Check(results.Order().SequenceEqual(new[] { 200, 409 }), "Concurrent connections reject stale overwrite");
        // A batch with a valid first day and a later overlap must also roll back the valid day.
        var conflictDay = created.First(s => s.DayOfWeek > 0 && s.DayOfWeek != target.DayOfWeek).DayOfWeek;
        var partial = batch with { DayOfWeeks = [0, conflictDay], StartTime = new(11, 0), EndTime = new(12, 0) };
        var sunday = await Fresh(s => s.ListAsync(rId, doctorId, default, true));
        var sundayRow = sunday.Single(s => s.DayOfWeek == 0);
        if (sundayRow.IsActive) await FreshVoid(s => s.DeleteAsync(rId, sundayRow.Id, default, true, sundayRow.Version));
        var before = await db.DoctorSchedules.CountAsync();
        await Expect(409, () => Fresh(s => s.CreateBatchAsync(rId, partial, default, true)), "Later overlap rolls back earlier valid batch day");
        Check(await db.DoctorSchedules.CountAsync() == before, "Batch rollback verified in real SQL");
        // Booking and deactivation compete on separate connections using the actual booking service.
        var raceShift = (await Fresh(s => s.CreateBatchAsync(rId, batch with { DayOfWeeks = [0], StartTime = new(14, 0), EndTime = new(16, 0) }, default, true))).Single();
        var raceDate = date.Date.AddDays((7 - (int)date.DayOfWeek) % 7 + 7).AddHours(15);
        async Task<int> Book()
        {
            await using var fresh = new FoMedDbContext(options);
            var response = await new AppointmentService(Work(fresh)).BookAppointmentForPatientAsync(rId,
                new StaffBookAppointmentRequest { PatientId = demoPatient.Id, DoctorId = doctorId, StartTime = raceDate, Source = 1 }, default);
            return response.StatusCode;
        }
        async Task<int> Stop()
        {
            try { await FreshVoid(s => s.DeleteAsync(rId, raceShift.Id, default, true, raceShift.Version)); return 204; }
            catch (ClinicException e) { return e.StatusCode; }
        }
        var bookingRace = await Task.WhenAll(Book(), Stop());
        Check((bookingRace[0] == 201 && bookingRace[1] == 409) || (bookingRace[0] == 400 && bookingRace[1] == 204), "Booking versus deactivation cannot both succeed");
        var raceBooking = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(a => a.StartTime == raceDate);
        var finalShift = await db.DoctorSchedules.AsNoTracking().SingleAsync(s => s.Id == raceShift.Id);
        Check(raceBooking == null || DoctorScheduleSafety.Covers(finalShift, raceBooking), "Concurrent booking never left without a working shift");
    }
    finally
    {
        await using var cleanup = new FoMedDbContext(options);
        await cleanup.Database.EnsureDeletedAsync();
        Console.WriteLine("Removed isolated local test DB: " + name);
    }
}
Console.WriteLine($"Reception schedules: {checks} checks passed.");

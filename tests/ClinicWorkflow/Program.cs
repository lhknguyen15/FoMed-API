using System.Text.RegularExpressions;
using FoMed.Application.DTO.Appointment;
using FoMed.Application.DTO.Clinical;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Billing;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

var supportedArguments = new HashSet<string>(StringComparer.Ordinal)
{
    "--sql", "--http", "--hosting-only", "--sepay-only", "--cash-only",
    "--dispensing-only", "--browser", "--journey-only", "--rate-limit-only"
};
if (args.Any(argument => !supportedArguments.Contains(argument)))
{
    Console.Error.WriteLine("Unsupported audit argument. Use --sql, --hosting-only, or --http with supported focus options.");
    Environment.ExitCode = 2;
    return;
}

var checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
var day = new DateTime(2030, 1, 7, 9, 0, 0);
Check(ClinicTime.IsSlot(day, new(9, 0), new(12, 0), 30), "First slot");
Check(!ClinicTime.IsSlot(day.AddMinutes(5), new(9, 0), new(12, 0), 30), "Off-grid slot");
Check(!ClinicTime.IsSlot(day.AddHours(3), new(9, 0), new(12, 0), 30), "End of shift");
Check(!ClinicTime.IsSlot(day.AddMinutes(150), new(9, 0), new(11, 45), 30), "Slot extends past shift");
Check(!ClinicTime.IsSlot(day.AddTicks(1), new(9, 0), new(12, 0), 30), "Fractional second");
Check(!ClinicTime.IsSlot(day, new(9, 0), new(12, 0), 0), "Invalid duration");
Check(ClinicTime.Normalize(new DateTime(2030, 1, 7, 2, 0, 0, DateTimeKind.Utc)) == day, "UTC to Vietnam");
Console.WriteLine($"PASS: {checks} time/slot checks.");
SePayAudit.RunUnitChecks();
if (args.Contains("--rate-limit-only")) { await AuthRateLimitAudit.RunIsolatedChecksAsync(); if (!args.Contains("--http")) return; }
if (args.Contains("--hosting-only")) { await CloudHostingAudit.RunAsync(); return; }
if (args.Contains("--http")) { await HttpWorkflowAudit.RunAsync(args); return; }
if (!args.Contains("--sql")) { Console.WriteLine("Use --sql from the repository root to run isolated SQL Server integration checks."); return; }

var name = "FoMed_Test_" + Guid.NewGuid().ToString("N");
var builder = LocalAuditDatabase.Master(name);
await using var admin = new SqlConnection(builder.ConnectionString);
await admin.OpenAsync();
await new SqlCommand($"CREATE DATABASE [{name}]", admin).ExecuteNonQueryAsync();
builder.InitialCatalog = name;
var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(builder.ConnectionString).Options;
FoMedDbContext Db() => new(options);
AppointmentService Appointments(FoMedDbContext db) => new(new UnitOfWork(db, new UserRepository(db), new PatientRepository(db), new DoctorRepository(db), new SpecialtyRepository(db), new AppointmentRepository(db), new DoctorScheduleRepository(db), new ServiceRepository(db)));
ClinicalService Clinical(FoMedDbContext db) { var repo = new ClinicRepository(db); return new(repo, new ClinicAccess(repo)); }
BillingService Billing(FoMedDbContext db) { var repo = new ClinicRepository(db); return new(repo, new ClinicAccess(repo)); }
async Task Expect(int status, Func<Task> action)
{
    try { await action(); throw new Exception($"Expected status {status}"); }
    catch (ClinicException e) { Check(e.StatusCode == status, $"Expected {status}, got {e.StatusCode}"); }
}
try
{
    await using (var db = Db())
    {
        var sql = File.ReadAllText("database/fomed-create-database.sql");
        sql = sql[sql.IndexOf("IF NOT EXISTS (SELECT 1 FROM sys.schemas", StringComparison.Ordinal)..];
        foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);

        var migration = Regex.Replace(File.ReadAllText("database/migrations/20260929_add_service_id_to_appointments.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (var batch in Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);

        var feeMigration = Regex.Replace(File.ReadAllText("database/migrations/20260930_add_fee_snapshot_to_appointments.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (var batch in Regex.Split(feeMigration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
        var attachmentMigration = Regex.Replace(File.ReadAllText("database/migrations/20261005_add_attachment_metadata.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        foreach (var batch in Regex.Split(attachmentMigration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
        var cashMigration = Regex.Replace(File.ReadAllText("database/migrations/20261005_add_payment_cash_audit.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (Regex.IsMatch(cashMigration, @"\bUSE\s+|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase)) throw new Exception("Cross-database SQL is prohibited.");
        foreach (var batch in Regex.Split(cashMigration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
        var sepayMigration = Regex.Replace(File.ReadAllText("database/migrations/20261005_add_sepay_payments.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        if (Regex.IsMatch(sepayMigration, @"\bUSE\s+|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase)) throw new Exception("Cross-database SQL is prohibited.");
        foreach (var batch in Regex.Split(sepayMigration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
    }
    int patientUser, otherPatientUser, doctorUser, otherDoctorUser, receptionistUser, technicianUser, doctorId, secondDoctorId, medicineId, serviceId, inactiveServiceId;
    var date = DateOnly.FromDateTime(ClinicTime.Now.AddDays(2));
    var start = date.ToDateTime(new TimeOnly(9, 0));
    await using (var db = Db())
    {
        var roles = await db.Roles.ToDictionaryAsync(r => r.Name);
        User User(string role, string suffix)
        {
            var user = new User { Username = suffix, Email = suffix + "@test.invalid", FullName = suffix, PasswordHash = "test-only", IsActive = true };
            user.UserRoles.Add(new FoMed.Infrastructure.Models.UserRole { Role = roles[role] }); db.Users.Add(user); return user;
        }
        var p = User("Patient", "patient"); var p2 = User("Patient", "patient2");
        p.Patient = new Patient { PatientCode = "TEST1", FullName = "Patient", IsActive = true };
        p2.Patient = new Patient { PatientCode = "TEST2", FullName = "Patient2", IsActive = true };
        var d = User("Doctor", "doctor"); var d2 = User("Doctor", "doctor2");
        var specialty = new Specialty { Name = "Test", IsActive = true };
        d.Doctor = new Doctor { FullName = "Doctor", Specialty = specialty, IsActive = true };
        d2.Doctor = new Doctor { FullName = "Doctor2", Specialty = specialty, IsActive = true };
        foreach (var doc in new[] { d.Doctor, d2.Doctor }) doc.DoctorSchedules.Add(new DoctorSchedule { DayOfWeek = (byte)date.DayOfWeek, StartTime = new(9, 0), EndTime = new(12, 0), SlotMinutes = 30, IsActive = true });
        var r = User("Receptionist", "receptionist"); var t = User("Technician", "technician");
        var med = new Medicine { Name = "Test medicine", Unit = "tablet", Price = 10, IsActive = true };
        med.MedicineBatches.Add(new MedicineBatch { LotNumber = "TEST-STOCK", Quantity = 100, ExpiryDate = date.AddYears(1), CreatedAt = DateTime.UtcNow });
        var svc = new Service { Name = "Test service", Price = 100, IsActive = true };
        var inactiveSvc = new Service { Name = "Inactive test service", Price = 200, IsActive = false };
        db.AddRange(med, svc, inactiveSvc);
        await db.SaveChangesAsync();
        patientUser = p.Id; otherPatientUser = p2.Id; doctorUser = d.Id; otherDoctorUser = d2.Id; receptionistUser = r.Id; technicianUser = t.Id;
        doctorId = d.Doctor.Id; secondDoctorId = d2.Doctor.Id; medicineId = med.Id; serviceId = svc.Id; inactiveServiceId = inactiveSvc.Id;
    }
    async Task<FoMed.Application.DTO.HTTPResponseData<AppointmentResponse?>> Book(int uid, int did, DateTime time, int? requestedServiceId = null)
    {
        await using var db = Db(); return await Appointments(db).BookAppointmentAsync(uid, new() { DoctorId = did, StartTime = time, ServiceId = requestedServiceId });
    }
    Check((await Book(patientUser, doctorId, start.AddMinutes(5))).StatusCode == 400, "Off-grid booking accepted");
    Check((await Book(patientUser, doctorId, start.AddMinutes(170))).StatusCode == 400, "Out-of-shift booking accepted");
    Check((await Book(patientUser, doctorId, ClinicTime.Now.AddDays(-1))).StatusCode == 400, "Past booking accepted");
    var linkedAppointment = await Book(patientUser, doctorId, start.AddHours(2), serviceId);
    Check(linkedAppointment.StatusCode == 201 && linkedAppointment.DataResponse?.ServiceId == serviceId && linkedAppointment.DataResponse.ServiceName == "Test service" &&
        linkedAppointment.DataResponse.FeeSnapshot == 100 && linkedAppointment.DataResponse.Source == 0,
        "Service, fee snapshot or source was not linked to appointment response");
    Check((await Book(patientUser, doctorId, start.AddHours(2.5), int.MaxValue)).StatusCode == 404, "Unknown service accepted");
    Check((await Book(patientUser, doctorId, start.AddHours(2.5), inactiveServiceId)).StatusCode == 404, "Inactive service accepted");
    await using (var db = Db())
    {
        var patientAppointments = (await Appointments(db).GetPatientAppointmentsAsync(patientUser)).DataResponse;
        var loadedLinkedAppointment = patientAppointments.Single(a => a.Id == linkedAppointment.DataResponse!.Id);
        Check(loadedLinkedAppointment.ServiceId == serviceId && loadedLinkedAppointment.ServiceName == "Test service", "Service relation missing from patient appointment query");
    }
    var concurrent = await Task.WhenAll(Book(patientUser, doctorId, start), Book(otherPatientUser, doctorId, start));
    Check(concurrent.Count(r => r.StatusCode == 201) == 1 && concurrent.Count(r => r.StatusCode == 409) == 1, "Concurrent booking not protected");
    var booked = concurrent.Single(r => r.StatusCode == 201).DataResponse!;
    var owner = concurrent[0].StatusCode == 201 ? patientUser : otherPatientUser;
    var stranger = owner == patientUser ? otherPatientUser : patientUser;
    Check((await Book(owner, secondDoctorId, start)).StatusCode == 409, "Patient overlap not protected");
    var more = await Task.WhenAll(Book(owner, doctorId, start.AddMinutes(30)), Book(stranger, doctorId, start.AddMinutes(60)));
    Check(more.All(r => r.StatusCode == 201 && r.DataResponse!.QueueNumber is null), "Queue number assigned before patient check-in");
    Check(more.Select(r => r.DataResponse!.AppointmentCode).Append(booked.AppointmentCode).Distinct().Count() == 3, "Duplicate appointment codes");
    await using (var db = Db()) Check((await Appointments(db).ConfirmAppointmentAsync(otherDoctorUser, booked.Id, new())).StatusCode == 403, "Foreign doctor confirmation");
    await using (var db = Db()) Check((await Appointments(db).ConfirmAppointmentAsync(receptionistUser, booked.Id, new())).StatusCode == 200, "Receptionist confirmation");
    await using (var db = Db()) Check((await Appointments(db).ConfirmAppointmentAsync(receptionistUser, booked.Id, new())).StatusCode == 400, "Repeated confirmation");
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 409, "Confirmed appointment completed before consultation");
    await using (var db = Db())
    {
        var inProgressWithoutRecord = await db.Appointments.SingleAsync(a => a.Id == booked.Id);
        inProgressWithoutRecord.Status = (byte)AppointmentStatus.InProgress;
        await db.SaveChangesAsync();
    }
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 409, "Appointment without medical record completed");
    await using (var db = Db())
    {
        var confirmed = await db.Appointments.SingleAsync(a => a.Id == booked.Id);
        confirmed.Status = (byte)AppointmentStatus.Confirmed;
        confirmed.StartTime = DateOnly.FromDateTime(ClinicTime.Now).ToDateTime(new TimeOnly(23, 0));
        confirmed.EndTime = confirmed.StartTime.AddMinutes(30);
        await db.SaveChangesAsync();
    }
    await using (var db = Db())
    {
        var checkedIn = await Appointments(db).CheckInAppointmentAsync(receptionistUser, booked.Id, new());
        Check(checkedIn.StatusCode == 200 && checkedIn.DataResponse?.QueueNumber == 1 && checkedIn.DataResponse.CheckedInAt.HasValue, "Reception check-in did not assign queue number");
    }
    await using (var db = Db()) Check((await Appointments(db).CheckInAppointmentAsync(receptionistUser, booked.Id, new())).StatusCode == 409, "Repeated check-in accepted");
    await using (var db = Db())
    {
        var queue = await Appointments(db).GetWaitingQueueAsync(receptionistUser, DateOnly.FromDateTime(ClinicTime.Now));
        Check(queue.StatusCode == 200 && queue.DataResponse?.Single().Id == booked.Id, "Waiting queue included unchecked appointments or omitted check-in");
    }
    MedicalRecordResponse record;
    await using (var db = Db()) record = await Clinical(db).CreateRecordAsync(doctorUser, booked.Id, new() { Symptoms = "Test" }, default);
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 409, "Appointment without diagnosis completed");
    await using (var db = Db()) await Clinical(db).UpdateRecordAsync(doctorUser, record.Id, new() { Symptoms = "Test", Diagnosis = "Test diagnosis" }, default);
    await using (var db = Db()) await Expect(403, () => Clinical(db).GetRecordAsync(stranger, record.Id, default));
    await using (var db = Db()) await Expect(403, () => Clinical(db).UpdateRecordAsync(otherDoctorUser, record.Id, new(), default));
    await using (var db = Db()) await Expect(403, () => Clinical(db).GetRecordAsync(owner, record.Id, default));
    await using (var db = Db()) await Clinical(db).CreatePrescriptionAsync(doctorUser, record.Id, new() { Items = [new() { MedicineId = medicineId, Quantity = 2, Dosage = "Test dosage" }] }, default);
    await using (var db = Db()) await Expect(409, () => Clinical(db).CreatePrescriptionAsync(doctorUser, record.Id, new() { Items = [new() { MedicineId = medicineId, Quantity = 1, Dosage = "Test" }] }, default));
    ServiceOrderResponse order;
    await using (var db = Db()) order = await Clinical(db).OrderServiceAsync(doctorUser, record.Id, new() { ServiceId = serviceId }, default);
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 409, "Appointment with pending order completed");
    await using (var db = Db()) await Expect(403, () => Clinical(db).SaveResultAsync(owner, order.Id, new() { ResultSummary = "Test" }, default));
    await using (var db = Db()) await Clinical(db).SaveResultAsync(technicianUser, order.Id, new() { ResultSummary = "Test" }, default);
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 200, "Completion");
    await using (var db = Db()) Check((await Clinical(db).GetRecordAsync(owner, record.Id, default)).Id == record.Id, "Patient finalized record access");
    await using (var db = Db()) Check((await Appointments(db).CompleteAppointmentAsync(doctorUser, booked.Id, new())).StatusCode == 409, "Repeated completion");
    await using (var db = Db()) Check((await Appointments(db).CancelAppointmentAsync(owner, booked.Id, new CancelAppointmentRequest { Reason = "Test" })).StatusCode == 400, "Cancelled completed appointment");
    var cancellable = more[0].DataResponse!;
    await using (var db = Db()) Check((await Appointments(db).CancelAppointmentAsync(stranger, cancellable.Id, new CancelAppointmentRequest { Reason = "Test" })).StatusCode == 403, "Stranger cancellation");
    await using (var db = Db()) Check((await Appointments(db).CancelAppointmentAsync(owner, cancellable.Id, new CancelAppointmentRequest { Reason = "Test" })).StatusCode == 200, "Owner cancellation");
    await using (var db = Db()) Check((await Appointments(db).ConfirmAppointmentAsync(receptionistUser, cancellable.Id, new())).StatusCode == 400, "Confirmed cancelled appointment");
    Check((await Book(stranger, doctorId, start.AddMinutes(30))).StatusCode == 201, "Cancelled slot was not released");
    await using (var db = Db()) Check((await Appointments(db).GetStatusHistoryAsync(stranger, booked.Id)).StatusCode == 403, "Stranger history access");
    await using (var db = Db()) await Expect(409, () => Clinical(db).UpdateRecordAsync(doctorUser, record.Id, new(), default));
    InvoiceResponse invoice;
    await using (var db = Db()) invoice = await Billing(db).CreateAsync(receptionistUser, new() { MedicalRecordId = record.Id }, default);
    Check(invoice.TotalAmount == 120, "Incorrect server-side total");
    Check(invoice.RemainingAmount == 120 && invoice.StatusName == "Chưa thanh toán", "Invoice balance/status projection is incorrect");
    await using (var db = Db()) await Expect(409, () => Billing(db).CreateAsync(receptionistUser, new() { MedicalRecordId = record.Id }, default));
    await using (var db = Db()) await Expect(403, () => Billing(db).GetAsync(stranger, invoice.Id, default));
    await using (var db = Db()) await Expect(400, () => Billing(db).PayAsync(receptionistUser, invoice.Id, new() { Amount = 121 }, default));
    async Task<bool> Pay()
    {
        await using var db = Db();
        try { await Billing(db).PayAsync(receptionistUser, invoice.Id, new() { Amount = 120 }, default); return true; }
        catch (ClinicException e) when (e.StatusCode == 409) { return false; }
    }
    Check((await Task.WhenAll(Pay(), Pay())).Count(ok => ok) == 1, "Concurrent payment was recorded twice");
    await using (var db = Db())
    {
        var paid = await Billing(db).GetAsync(owner, invoice.Id, default);
        Check(paid.Status == 1 && paid.PaidAmount == 120 && paid.RemainingAmount == 0 && paid.StatusName == "Đã thanh toán" && paid.Payments.Count == 1, "Incorrect final payment");
        Check(await db.AppointmentStatusHistories.CountAsync(h => h.AppointmentId == booked.Id) == 5, "Incorrect transition history");
    }
    Console.WriteLine($"PASS: {checks} checks including SQL concurrency, clinical ownership, prescriptions, lab results and billing.");
}
finally
{
    SqlConnection.ClearAllPools();
    // Only the uniquely named database created by this run is ever removed.
    LocalAuditDatabase.Guard(builder, name);
    await new SqlCommand($"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]", admin).ExecuteNonQueryAsync();
}

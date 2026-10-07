using System.Text;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

// Fixtures live only in RAM. No appsettings, credentials, HTTP or application database access.
var options = new DbContextOptionsBuilder<FoMedDbContext>()
    .UseInMemoryDatabase("ReportNames-" + Guid.NewGuid()).Options;
var day = new DateTime(2026, 10, 6);
var paidAt = day.AddHours(14).AddHours(-7); // Financial timestamps are UTC; schedule timestamps are Vietnam local.
var query = new ReportQuery(day, day.AddDays(1), null);
var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception(label);
    checks++;
    Console.WriteLine("PASS: " + label);
}

await using (var db = new FoMedDbContext(options))
{
    var adminRole = new Role { Id = 1, Name = "Admin" };
    var receptionRole = new Role { Id = 2, Name = "Receptionist" };
    User Actor(int id, Role? role, bool active = true) => new()
    {
        Id = id, Username = "report-demo-" + id, FullName = "Account DEMO",
        PasswordHash = "not-a-credential", IsActive = active,
        UserRoles = role == null ? [] : [new UserRole { Id = id, Role = role }]
    };
    db.AddRange(Actor(1, adminRole), Actor(2, receptionRole), Actor(3, null), Actor(4, adminRole, false));
    var specialty = new Specialty { Id = 1, Name = "Specialty DEMO", IsActive = true };
    Doctor Doctor(int id, string name, bool active = true) => new()
    {
        Id = id, FullName = name, IsActive = active, Specialty = specialty, User = Actor(100 + id, null)
    };
    var current = Doctor(1, "Phạm Văn An DEMO");
    var historical = Doctor(8, "Nguyễn Thị Bình DEMO");
    var inactive = Doctor(9, "Trần Văn Cường DEMO", false);
    var issuedOnly = Doctor(10, "Lê Thị Dung DEMO");
    var unrelated = Doctor(11, "Không có hoạt động DEMO");
    var canceledOnly = Doctor(12, "Hóa đơn đã hủy DEMO");
    var patient = new Patient { Id = 1, PatientCode = "DEMO-REPORT", FullName = "Patient DEMO", IsActive = true };
    Appointment Visit(int id, Doctor doctor, DateTime at, byte status = 3) => new()
    {
        Id = id, AppointmentCode = "AP-DEMO-" + id, Doctor = doctor, Patient = patient,
        StartTime = at, EndTime = at.AddMinutes(30), Status = status
    };
    Invoice Bill(int id, Appointment? visit, decimal amount, DateTime issuedAt, byte status) => new()
    {
        Id = id, InvoiceNo = "HD-DEMO-" + id, Patient = patient, Appointment = visit,
        TotalAmount = amount, CreatedAt = issuedAt, Status = status
    };
    Payment Receipt(int id, Invoice invoice, decimal amount, DateTime at, bool sepay = false) => new()
    {
        Id = id, Invoice = invoice, Amount = amount, PaidAt = at, Method = sepay ? (byte)2 : (byte)1,
        CashReceived = sepay ? null : amount + 28_000m,
        Provider = sepay ? "SePay" : null, ProviderEnvironment = sepay ? "Test" : null,
        ProviderTransactionId = sepay ? id : null
    };
    var currentBill = Bill(1, Visit(1, current, day.AddHours(13)), 272_000m, paidAt.AddMinutes(-10), 1);
    var historicalBill = Bill(8, Visit(8, historical, day.AddDays(-8)), 261_000m, paidAt.AddDays(-8), 1);
    var partialBill = Bill(9, Visit(9, inactive, day.AddDays(-5)), 100_000m, paidAt.AddHours(-1), 0);
    var issuedBill = Bill(10, Visit(10, issuedOnly, day.AddDays(-3)), 50_000m, paidAt.AddHours(-1), 0);
    var canceledBill = Bill(12, Visit(12, canceledOnly, day.AddDays(-3)), 10_000m, paidAt.AddHours(-1), 2);
    var previousBill = Bill(13, Visit(13, historical, day.AddDays(-8)), 1_000m, paidAt.AddDays(-8), 1);
    var unassignedBill = Bill(20, null, 7_000m, paidAt.AddHours(-1), 0);
    db.AddRange(currentBill, historicalBill, partialBill, issuedBill, canceledBill, previousBill, unassignedBill, unrelated);
    db.AddRange(Visit(2, current, day.AddHours(14), 5), Visit(3, current, day.AddHours(15), 4));
    db.AddRange(Receipt(1, currentBill, 272_000m, paidAt), Receipt(8, historicalBill, 261_000m, paidAt, true),
        Receipt(9, partialBill, 20_000m, paidAt), Receipt(12, canceledBill, 10_000m, paidAt),
        Receipt(13, previousBill, 1_000m, paidAt.AddDays(-1)), Receipt(20, unassignedBill, 2_000m, paidAt));
    await db.SaveChangesAsync();
}

ReportService Reports(FoMedDbContext db)
{
    var repo = new ClinicRepository(db);
    return new ReportService(repo, new ClinicAccess(repo));
}
async Task Reject(ReportService service, int userId, ReportQuery filter, int status, string label)
{
    try { await service.SummaryAsync(userId, filter, default); throw new Exception("Unexpected access: " + label); }
    catch (ClinicException ex) { Check(ex.StatusCode == status, label); }
}
await using (var db = new FoMedDbContext(options))
{
    var service = Reports(db);
    var report = await service.SummaryAsync(1, query, default);
    var historical = report.Doctors.Single(d => d.DoctorId == 8);
    Check(historical.DoctorName == "Nguyễn Thị Bình DEMO", "Old invoice paid today resolves doctor name without today's appointment");
    Check(historical.AppointmentCount == 0 && historical.CompletedCount == 0, "Payment does not invent an appointment/completed visit in the period");
    Check(historical.CollectedAmount == 261_000m && historical.InvoicedAmount == 0 && historical.OutstandingAmount == 0,
        "SePay payment uses PaidAt, not old appointment or invoice creation dates");
    Check(report.Doctors.Single(d => d.DoctorId == 1).CollectedAmount + historical.CollectedAmount == 533_000m,
        "Cash plus SePay reproduces 533000 collected; customer tender/change do not inflate revenue");
    var partial = report.Doctors.Single(d => d.DoctorId == 9);
    Check(partial.DoctorName == "Trần Văn Cường DEMO" && partial.AppointmentCount == 0,
        "Inactive doctor's historical financial activity retains their full name");
    Check(partial.InvoicedAmount == 100_000m && partial.CollectedAmount == 20_000m && partial.OutstandingAmount == 80_000m,
        "Partial receipt and remaining debt stay unchanged");
    Check(report.Doctors.Single(d => d.DoctorId == 10).DoctorName == "Lê Thị Dung DEMO",
        "Doctor with invoice/debt only gets full name without scheduled visit in period");
    Check(report.Doctors.Select(d => d.DoctorId).Order().SequenceEqual(new[] { 1, 8, 9, 10 }),
        "Only relevant doctors included; unrelated and canceled-invoice-only doctors excluded");
    Check(report.TotalAppointments == 3 && report.CompletedAppointments == 1 && report.NoShowAppointments == 1
        && report.CancelledAppointments == 1 && report.NoShowRatePercent == 50m, "Appointment and no-show metrics are unchanged");
    Check(report.Doctors.Single(d => d.DoctorId == 1).NoShowRatePercent == 50m, "Per-doctor no-show denominator still excludes canceled visits");
    Check(report.InvoicedAmount == 429_000m && report.CollectedAmount == 555_000m && report.OutstandingAmount == 135_000m,
        "Totals include unassigned invoice but exclude canceled invoice and out-of-period payment");
    Check(report.Doctors.All(d => !d.DoctorName.StartsWith("Bác sĩ #")), "All valid doctor names resolve from profiles");
    var filtered = await service.SummaryAsync(1, query with { DoctorId = 8 }, default);
    Check(filtered.Doctors.Count == 1 && filtered.Doctors[0].DoctorName == historical.DoctorName && filtered.CollectedAmount == 261_000m
        && filtered.TotalAppointments == 0 && filtered.InvoicedAmount == 0 && filtered.OutstandingAmount == 0,
        "Doctor-filtered report resolves name and does not mix another doctor's money");
    var csv = await service.ExportCsvAsync(1, query, default);
    var csvText = Encoding.UTF8.GetString(csv.Content);
    Check(csvText.Contains("8,\"Nguyễn Thị Bình DEMO\",0,0,0,0,0,0,261000,0") && !csvText.Contains("Bác sĩ #8"),
        "CSV uses same correct name and payment figures as summary");
    Check(csv.FileName == "fomed-report-20261006-20261006.csv", "Export period/date semantics unchanged");
    var filteredCsv = Encoding.UTF8.GetString((await service.ExportCsvAsync(1, query with { DoctorId = 8 }, default)).Content);
    Check(filteredCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length == 3 && !filteredCsv.Contains("Phạm Văn An DEMO"),
        "Filtered CSV contains only total and requested doctor");
    var empty = await service.SummaryAsync(1, new ReportQuery(day.AddDays(2), day.AddDays(3), null), default);
    Check(empty.Doctors.Count == 0 && empty.CollectedAmount == 0 && empty.OutstandingAmount == 0 && empty.TotalAppointments == 0,
        "Empty period returns empty report rather than all doctor profiles");
    var unknown = await service.SummaryAsync(1, query with { DoctorId = 999 }, default);
    Check(unknown.Doctors.Count == 0 && unknown.CollectedAmount == 0, "Unknown doctor filter does not leak other rows");
    Check((await service.SummaryAsync(2, query, default)).CollectedAmount == report.CollectedAmount,
        "Receptionist retains report access alongside Admin");
    await Reject(service, 3, query, 403, "User without report role is denied");
    await Reject(service, 4, query, 403, "Inactive admin is denied");
    await Reject(service, 999, query, 403, "Unknown account is denied");
    await Reject(service, 1, new ReportQuery(day, day, null), 400, "Invalid period is still rejected");
    Check(!db.ChangeTracker.Entries().Any(), "Report reads do not track or mutate database entities");
    Check(await db.Payments.CountAsync() == 6 && await db.Invoices.CountAsync() == 7, "Summary/export create no payments or invoices");
}
// Translation only: constructing SQL is not opening a connection to this intentionally unreachable server.
var sqlOptions = new DbContextOptionsBuilder<FoMedDbContext>()
    .UseSqlServer("Server=127.0.0.1,1;Database=ReportQueryOnly;Integrated Security=true;Encrypt=true;TrustServerCertificate=False").Options;
await using (var db = new FoMedDbContext(sqlOptions))
{
    int[] ids = [1, 8, 9, 10];
    var sql = db.Doctors.AsNoTracking().Where(d => ids.Contains(d.Id)).Select(d => new { d.Id, d.FullName }).ToQueryString();
    Check(sql.Contains("full_name") && sql.Contains("WHERE") && !sql.Contains("is_active"),
        "Batched name lookup pattern translates to SQL Server without excluding inactive doctors");
}
await using (var db = new FoMedDbContext(options))
{
    var repeated = await Reports(db).SummaryAsync(1, query with { DoctorId = 8 }, default);
    Check(repeated.Doctors[0].DoctorName == "Nguyễn Thị Bình DEMO" && repeated.CollectedAmount == 261_000m,
        "Fresh request returns same name and money without previous tracked entities");
}
Console.WriteLine($"Report doctor name audit: {checks} passed. InMemory and SQL translation only; no local/cloud DB or deployed HTTP.");

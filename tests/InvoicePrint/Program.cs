using System.Text.Json;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Read-only service checks, synthetic seed only. Never read private appsettings or Azure.
if (args.Any(a => a != "--sql")) throw new ArgumentException("Only --sql is supported.");
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
async Task Verify(DbContextOptions<FoMedDbContext> options, string mode)
{
    var roles = new Dictionary<string, Role>();
    User Actor(string account, string role, bool active = true)
    {
        if (!roles.TryGetValue(role, out var entity)) roles[role] = entity = new Role { Name = role };
        return new User { Username = account, FullName = "Account DEMO", PasswordHash = "not-a-password", IsActive = active, UserRoles = [new UserRole { Role = entity }] };
    }
    int adminId, staffId, ownerId, otherId, doctorId, inactiveId, invoiceId, legacyId, freeId, simulatedId;
    var createdUtc = new DateTime(2026, 10, 6, 17, 0, 0, DateTimeKind.Unspecified);
    await using (var seed = new FoMedDbContext(options))
    {
        var admin = Actor("admin-demo", "Admin"); var staff = Actor("cashier-demo", "Receptionist");
        var owner = Actor("owner-demo", "Patient"); var other = Actor("other-demo", "Patient");
        var doctor = Actor("doctor-demo", "Doctor"); var inactive = Actor("inactive-demo", "Receptionist", false);
        var patient = new Patient { PatientCode = "BN-PRINT-DEMO", FullName = "Tên mới DEMO", IsActive = true, User = owner };
        var bill = new Invoice { InvoiceNo = "HD-PRINT-DEMO", Patient = patient, PatientName = "Tên lúc lập hóa đơn DEMO", CreatedAt = createdUtc,
            TotalAmount = 301000m, ConsultationFee = 150000m, Status = 0,
            InvoiceItems = [new InvoiceItem { Description = "Dịch vụ DEMO", Quantity = 1, UnitPrice = 151000m, Amount = 151000m }],
            Payments = [new Payment { Amount = 200000m, Method = 0, CashReceived = 250000m, ReceivedBy = null, ReceivedByNameSnapshot = "Lễ tân lúc thu DEMO", PaidAt = createdUtc.AddHours(1), IdempotencyKey = Guid.NewGuid() }] };
        var legacy = new Invoice { InvoiceNo = "HD-LEGACY-DEMO", Patient = patient, CreatedAt = createdUtc, TotalAmount = 50000m, ConsultationFee = 50000m, Status = 0 };
        var free = new Invoice { InvoiceNo = "HD-FREE-DEMO", Patient = patient, CreatedAt = createdUtc, TotalAmount = 0m, ConsultationFee = 0m, Status = 1,
            InvoiceItems = [new InvoiceItem { Description = "Miễn phí DEMO", Quantity = 2, UnitPrice = 0m, Amount = 0m }] };
        var simulated = new Invoice { InvoiceNo = "HD-SEPAY-DEMO", Patient = patient, CreatedAt = createdUtc, TotalAmount = 261000m, Status = 1,
            InvoiceItems = [new InvoiceItem { Description = "Dịch vụ thử DEMO", Quantity = 1, UnitPrice = 261000m, Amount = 261000m }],
            Payments = [new Payment { Amount = 261000m, Method = 2, PaidAt = createdUtc.AddHours(2), Provider = "SePay", ProviderEnvironment = "Test", ProviderTransactionId = 177 }] };
        seed.AddRange(admin, staff, other, doctor, inactive, bill, legacy, free, simulated);
        await seed.SaveChangesAsync();
        adminId = admin.Id; staffId = staff.Id; ownerId = owner.Id; otherId = other.Id; doctorId = doctor.Id; inactiveId = inactive.Id;
        invoiceId = bill.Id; legacyId = legacy.Id; freeId = free.Id; simulatedId = simulated.Id;
    }
    await using var db = new FoMedDbContext(options);
    var repo = new ClinicRepository(db); var service = new BillingService(repo, new ClinicAccess(repo));
    foreach (var actor in new[] { adminId, staffId, ownerId })
    {
        var result = await service.GetAsync(actor, invoiceId, default);
        Check(result.PatientName == "Tên lúc lập hóa đơn DEMO" && result.PatientCode == "BN-PRINT-DEMO", mode + ": authorized reader gets snapshot identity, not renamed profile");
        Check(result.CreatedAt is { Kind: DateTimeKind.Utc } && result.CreatedAt.Value.Hour == 17, mode + ": created time explicitly UTC, never shifted twice");
    }
    foreach (var actor in new[] { otherId, doctorId, inactiveId, 99999 })
    {
        try { await service.GetAsync(actor, invoiceId, default); throw new Exception("Unexpected access"); }
        catch (ClinicException error) { Check(error.StatusCode == 403, mode + ": unauthorized/inactive reader gets no printable invoice"); }
    }
    var detail = await service.GetAsync(staffId, invoiceId, default);
    Check(detail.PaidAmount == 200000m && detail.RemainingAmount == 101000m && detail.Payments.Single().ChangeAmount == 50000m, mode + ": saved payment sum excludes tender/change");
    Check(detail.Payments.Single().ReceivedByName == "Lễ tân lúc thu DEMO", mode + ": original cashier snapshot retained");
    Check(detail.Items.Single().UnitPrice == 151000m && detail.ConsultationFee == 150000m, mode + ": saved invoice prices and separate consultation fee retained");
    var old = await service.GetAsync(staffId, legacyId, default);
    Check(old.PatientName == "Tên mới DEMO" && old.PatientCode == "BN-PRINT-DEMO", mode + ": legacy invoice without name snapshot gets explicit current profile fallback");
    var noCharge = await service.GetAsync(staffId, freeId, default);
    Check(noCharge.TotalAmount == 0 && noCharge.PaidAmount == 0 && noCharge.Status == 1 && noCharge.Items.Single().Amount == 0, mode + ": zero-price settled invoice preserved");
    var testPayment = await service.GetAsync(ownerId, simulatedId, default);
    Check(testPayment.Payments.Single().ProviderEnvironment == "Test" && testPayment.Payments.Single().ProviderTransactionId == 177, mode + ": Test marker remains available for print warning");
    var json = JsonSerializer.Serialize(detail, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    Check(json.Contains("\"createdAt\":\"2026-10-06T17:00:00Z\"") && json.Contains("\"patientName\"") && json.Contains("\"patientCode\""), mode + ": response JSON contains compatible print header fields and UTC suffix");
    Check(!json.Contains("passwordHash", StringComparison.OrdinalIgnoreCase) && !json.Contains("\"patient\":"), mode + ": response never serializes user profile/password object");
    Check(await db.Invoices.CountAsync() == 4 && await db.Payments.CountAsync() == 2 && db.ChangeTracker.Entries().All(e => e.State == EntityState.Unchanged), mode + ": reading print data never changes invoices or payments");
}

await Verify(new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("InvoicePrint-" + Guid.NewGuid()).Options, "Memory");
if (args.Contains("--sql"))
{
    var name = "FoMed_Print_Test_" + Guid.NewGuid().ToString("N");
    var connection = new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = name, IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 5 };
    if (connection.DataSource != "localhost" || connection.InitialCatalog != name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^FoMed_Print_Test_[a-f0-9]{32}$")) throw new Exception("Unsafe test DB target.");
    var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(connection.ConnectionString).Options;
    try
    {
        await using (var seed = new FoMedDbContext(options)) await seed.Database.EnsureCreatedAsync();
        await Verify(options, "SQL");
    }
    finally
    {
        await using var cleanup = new FoMedDbContext(options);
        await cleanup.Database.EnsureDeletedAsync(); Console.WriteLine("Removed isolated local test DB: " + name);
    }
}
Console.WriteLine($"Invoice print backend: {checks} checks passed.");

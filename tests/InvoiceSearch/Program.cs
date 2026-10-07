using System.Reflection;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

// Synthetic data in RAM only. No appsettings, HTTP, credentials or Azure access.
var options = new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("InvoiceSearch-" + Guid.NewGuid()).Options;
var checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
BillingService Service(FoMedDbContext db) { var repo = new ClinicRepository(db); return new BillingService(repo, new ClinicAccess(repo)); }
var day = new DateOnly(2026, 10, 7);
var startUtc = day.ToDateTime(TimeOnly.MinValue).AddHours(-7);
await using (var db = new FoMedDbContext(options))
{
    User Actor(int id, string? role, bool active = true) => new() { Id = id, Username = "invoice-demo-" + id, FullName = "Account DEMO", PasswordHash = "not-a-credential", IsActive = active, UserRoles = role == null ? [] : [new UserRole { Id = id, Role = new Role { Id = id, Name = role } }] };
    db.AddRange(Actor(1, "Admin"), Actor(2, "Receptionist"), Actor(3, "Doctor"), Actor(4, null), Actor(5, "Admin", false));
    var patient = new Patient { Id = 1, PatientCode = "BN-DEMO-01", FullName = "Nguyễn An DEMO", IsActive = true };
    Invoice Bill(int id, DateTime createdAt, byte status = 0) => new() { Id = id, InvoiceNo = "HD-DEMO-" + id.ToString("D4"), Patient = patient, CreatedAt = createdAt, Status = status, TotalAmount = 100_000m };
    for (var id = 1; id <= 45; id++) db.Add(Bill(id, startUtc.AddMinutes(id)));
    var partial = Bill(46, startUtc.AddHours(2)); partial.Payments.Add(new Payment { Id = 1, Amount = 20_000m, PaidAt = startUtc.AddHours(3) }); db.Add(partial);
    var paid = Bill(47, startUtc.AddHours(3), 1); paid.Payments.Add(new Payment { Id = 2, Amount = 100_000m, PaidAt = startUtc.AddHours(4), Provider = "SePay", ProviderEnvironment = "Test", ProviderTransactionId = 100 }); db.Add(paid);
    db.Add(Bill(48, startUtc.AddHours(4), 2));
    db.Add(Bill(49, startUtc.AddTicks(-1)));
    db.Add(Bill(50, startUtc.AddDays(1).AddTicks(-1)));
    db.Add(Bill(51, startUtc.AddDays(1)));
    var historical = Bill(52, startUtc.AddDays(-1)); historical.PatientName = "Tên tại thời điểm lập DEMO"; db.Add(historical);
    await db.SaveChangesAsync();
}
await using (var db = new FoMedDbContext(options))
{
    var service = Service(db);
    var all = new InvoiceSearchRequest();
    var first = await service.SearchAsync(1, all, default);
    Check(first.TotalCount == 52 && first.Items.Count == 20 && first.PageSize == 20, "Counts entire dataset before taking 20 rows");
    var second = await service.SearchAsync(2, all with { Page = 2 }, default);
    var last = await service.SearchAsync(2, all with { Page = 3 }, default);
    Check(second.Items.Count == 20 && last.Items.Count == 12 && first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)).Count() == 0, "Stable pagination with Admin and Receptionist access");
    var old = await service.SearchAsync(2, all with { Keyword = "HD-DEMO-0001" }, default);
    Check(old.TotalCount == 1 && old.Items.Single().Id == 1, "Search finds invoice outside first page");
    foreach (var keyword in new[] { "Nguyễn An", "BN-DEMO-01", "#1" })
        Check((await service.SearchAsync(1, all with { Keyword = keyword }, default)).TotalCount == (keyword == "Nguyễn An" ? 51 : 52), "Patient lookup: " + keyword);
    Check((await service.SearchAsync(1, all with { Keyword = "Tên tại thời điểm" }, default)).Items.Single().Id == 52, "Uses invoice patient-name snapshot");
    foreach (var pair in new[] { ("unpaid", 49), ("partial", 1), ("paid", 1), ("cancelled", 1), ("outstanding", 50) })
        Check((await service.SearchAsync(1, all with { Status = pair.Item1 }, default)).TotalCount == pair.Item2, "Correct payment filter: " + pair.Item1);
    var partial = (await service.SearchAsync(2, all with { Status = "partial" }, default)).Items.Single();
    Check(partial.PaidAmount == 20_000m && partial.TotalAmount == 100_000m, "Payment sum does not use tender or inflate invoice amount");
    var dated = await service.SearchAsync(1, all with { FromDate = day, ToDate = day, Page = 3 }, default);
    Check(dated.TotalCount == 49 && dated.Items.All(i => i.CreatedAt >= startUtc && i.CreatedAt < startUtc.AddDays(1)), "Vietnam calendar day maps to inclusive UTC start/exclusive end");
    Check(dated.Items.Any(i => i.Id == 1) && !dated.Items.Any(i => i.Id == 49), "Correct midnight boundaries, including earliest eligible invoices");
    Check(first.Items.All(i => i.CreatedAt.Kind == DateTimeKind.Utc), "API timestamps explicitly identify UTC");
    Check((await service.SearchAsync(1, all with { Keyword = "not found DEMO" }, default)).TotalCount == 0, "Empty filter result, not unfiltered fallback");
    Check((await service.SearchAsync(1, all with { Page = 100000 }, default)).Items.Count == 0, "Out-of-range dataset page remains bounded");
    foreach (var actor in new[] { 3, 4, 5, 999 })
    {
        try { await service.SearchAsync(actor, all, default); throw new Exception("Unexpected access"); }
        catch (ClinicException error) { Check(error.StatusCode == 403, "Rejects non-cashier/inactive/missing actor " + actor); }
    }
    foreach (var invalid in new[] { all with { Page = 0 }, all with { Page = 100001 }, all with { Status = "invalid" }, all with { Keyword = new string('x', 101) }, all with { FromDate = day.AddDays(1), ToDate = day }, all with { ToDate = DateOnly.MaxValue }, all with { FromDate = DateOnly.MinValue } })
    {
        try { await service.SearchAsync(1, invalid, default); throw new Exception("Unexpected filter acceptance"); }
        catch (ClinicException error) { Check(error.StatusCode == 400, "Rejects invalid filter safely"); }
    }
    Check(db.ChangeTracker.Entries().Count() == 0 && await db.Set<Invoice>().CountAsync() == 52 && await db.Set<Payment>().CountAsync() == 2, "Read-only search does not track or alter invoices/payments");
}
// SQL translation only; this dummy connection is never opened.
await using (var db = new FoMedDbContext(new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=OfflineTranslation;Integrated Security=true;TrustServerCertificate=true").Options))
{
    var service = Service(db);
    var build = typeof(BillingService).GetMethod("BuildSearchQuery", BindingFlags.Instance | BindingFlags.NonPublic)!;
    foreach (var status in new[] { "all", "outstanding", "unpaid", "partial", "paid", "cancelled" })
    {
        var query = (IQueryable<Invoice>)build.Invoke(service, [new InvoiceSearchRequest { Status = status, Keyword = "DEMO", FromDate = day, ToDate = day }])!;
        var sql = query.OrderByDescending(i => i.CreatedAt).ThenByDescending(i => i.Id).Skip(20).Take(20)
            .Select(i => new InvoiceSummaryResponse(i.Id, i.InvoiceNo, i.PatientId, i.Patient.PatientCode, i.PatientName ?? i.Patient.FullName, i.MedicalRecordId, i.CreatedAt, i.TotalAmount, i.Payments.Sum(p => (decimal?)p.Amount) ?? 0m, i.Status)).ToQueryString();
        Check(sql.Contains("WHERE") && sql.Contains("OFFSET") && sql.Contains("SUM"), "SQL Server translates filtered/paged payment projection: " + status);
    }
}
Console.WriteLine($"Invoice search: {checks} checks passed. Offline synthetic tests; not deployed E2E.");

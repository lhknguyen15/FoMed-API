using System.ComponentModel.DataAnnotations;
using System.Reflection;
using FoMed.Api.Controllers;
using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Pharmacy;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

// Never read appsettings or connect to Azure. SQL mode creates only a random local test DB.
if (args.Any(arg => arg != "--sql")) throw new ArgumentException("Only --sql is supported.");
var checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS: " + label); }
MedicineCatalogAdminService Service(FoMedDbContext db) { var repo = new ClinicRepository(db); return new(repo, new ClinicAccess(repo)); }
async Task Expect(int status, Func<Task> operation, string label)
{
    try { await operation(); throw new Exception("Expected rejection: " + label); }
    catch (ClinicException e) { Check(e.StatusCode == status, label); }
}

Check(typeof(MedicineCatalogAdminController).GetCustomAttribute<AuthorizeAttribute>()?.Roles == "Admin", "Controller restricted to Admin");
Check(!typeof(MedicineCatalogAdminController).GetMethods().Any(m => m.GetCustomAttributes().Any(a => a.GetType().Name == "HttpDeleteAttribute")), "No destructive delete endpoint");
foreach (var request in new object[] { new SaveMedicineRequest(), new UpdateMedicineRequest { Name = "A", Unit = "viên", Price = 1 }, new MedicineStatusRequest { ExpectedVersion = new string('A', 64) }, new MedicineStatusRequest { IsActive = false, ExpectedVersion = "wrong" } })
    Check(!Validator.TryValidateObject(request, new ValidationContext(request), [], true), "DTO rejects missing/invalid required fields");

var memoryOptions = new DbContextOptionsBuilder<FoMedDbContext>().UseInMemoryDatabase("MedicineCatalog-" + Guid.NewGuid()).Options;
await using (var db = new FoMedDbContext(memoryOptions))
{
    var role = new Role { Name = "Admin" };
    var admin = new User { Username = "demo-admin", FullName = "Admin DEMO", PasswordHash = "not-a-password", IsActive = true, UserRoles = [new UserRole { Role = role }] };
    db.Add(admin); await db.SaveChangesAsync();
    var today = DateOnly.FromDateTime(FoMed.Application.Services.Appointment.ClinicTime.Now);
    for (var i = 1; i <= 45; i++) db.Add(new Medicine { Name = $"Thuốc DEMO {i:D3}", Unit = "viên", Price = i, IsActive = i != 45,
        MedicineBatches = i == 1 ? [new MedicineBatch { LotNumber = "VALID", Quantity = 10, ExpiryDate = today }, new MedicineBatch { LotNumber = "OLD", Quantity = 7, ExpiryDate = today.AddDays(-1) }] : [] });
    await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    var service = Service(db);
    var first = await service.ListAsync(admin.Id, new(), default);
    var last = await service.ListAsync(admin.Id, new() { Page = 3 }, default);
    Check(first.TotalCount == 45 && first.Items.Count == 20 && last.Items.Count == 5, "Server count and pagination cover complete catalog");
    Check(!first.Items.Select(m => m.Id).Intersect(last.Items.Select(m => m.Id)).Any(), "Stable nonoverlapping pages");
    Check(first.Items[0].StockQuantity == 17 && first.Items[0].AvailableQuantity == 10, "Stock includes expired batches but availability excludes them");
    Check(first.Items.All(m => m.Version.Length == 64), "Catalog returns edit versions");
    Check((await service.ListAsync(admin.Id, new() { Keyword = "045", Status = "inactive" }, default)).Items.Single().Name.EndsWith("045"), "Search and status run before pagination");
    Check((await service.ListAsync(admin.Id, new() { Status = "active" }, default)).TotalCount == 44, "Active status filter");
    Check((await service.ListAsync(admin.Id, new() { Keyword = "missing DEMO" }, default)).TotalCount == 0, "Empty result remains empty");
    Check((await service.ListAsync(admin.Id, new() { Page = 99999 }, default)).Items.Count == 0, "Out-of-range page bounded safely");
    Check((await service.GetAsync(admin.Id, first.Items[0].Id, default)).Name == first.Items[0].Name, "Admin detail lookup");
    await Expect(404, () => service.GetAsync(admin.Id, 99999, default), "Unknown medicine not found");
    await Expect(403, () => service.ListAsync(99999, new(), default), "Missing user rejected");
    foreach (var invalid in new[] { new MedicineCatalogQuery { Page = 0 }, new() { Page = 100001 }, new() { Status = "other" }, new() { Keyword = new string('x', 101) }, new() { Keyword = "bad\n" + "query" } })
        await Expect(400, () => service.ListAsync(admin.Id, invalid, default), "Invalid search rejected");
    Check(db.ChangeTracker.Entries().Count() == 0, "Catalog reads do not track/write entities");
}
// Translation checks never open this dummy SQL connection.
await using (var db = new FoMedDbContext(new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer("Server=127.0.0.1,1;Database=OfflineTranslation;Integrated Security=true;TrustServerCertificate=true").Options))
{
    var service = Service(db);
    var build = typeof(MedicineCatalogAdminService).GetMethod("BuildQuery", BindingFlags.NonPublic | BindingFlags.Instance)!;
    var query = (IQueryable<Medicine>)build.Invoke(service, [new MedicineCatalogQuery { Keyword = "DEMO", Status = "active" }])!;
    Check(query.OrderBy(m => m.Name).ThenBy(m => m.Id).Skip(20).Take(20).ToQueryString().Contains("OFFSET"), "SQL Server translates filtered pagination");
    var pending = (IQueryable<PrescriptionItem>)typeof(MedicineCatalogAdminService).GetMethod("PendingPrescriptionItems", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [1])!;
    Check(pending.ToQueryString().Contains("SUM"), "SQL Server translates pending dispense guard");
}

if (args.Contains("--sql"))
{
    var name = "FoMed_Medicine_Test_" + Guid.NewGuid().ToString("N");
    var connection = new SqlConnectionStringBuilder { DataSource = "localhost", InitialCatalog = name,
        IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 5 };
    var options = new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(connection.ConnectionString).Options;
    // Guard the exact creation/removal target before any database operation.
    if (connection.DataSource != "localhost" || connection.InitialCatalog != name || !System.Text.RegularExpressions.Regex.IsMatch(name, "^FoMed_Medicine_Test_[a-f0-9]{32}$")) throw new Exception("Unsafe test database target.");
    try
    {
        await using var db = new FoMedDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var adminRole = new Role { Name = "Admin" };
        User Actor(string role, bool active = true) => new() { Username = "demo-" + role + (active ? "" : "-inactive"), FullName = "Account DEMO", PasswordHash = "not-a-password", IsActive = active,
            UserRoles = [new UserRole { Role = role == "Admin" ? adminRole : new Role { Name = role } }] };
        var admin = Actor("Admin"); var inactive = Actor("Admin", false); var pharmacist = Actor("Pharmacist"); var receptionist = Actor("Receptionist");
        db.AddRange(admin, inactive, pharmacist, receptionist); await db.SaveChangesAsync();
        var service = Service(db);
        foreach (var actor in new[] { inactive.Id, pharmacist.Id, receptionist.Id, 99999 })
        {
            await Expect(403, () => service.CreateAsync(actor, new() { Name = "Forbidden", Unit = "viên", Price = 10 }, default), "Create rejects unauthorized/inactive user");
            await Expect(403, () => service.UpdateAsync(actor, 1, new(), default), "Update rejects unauthorized user before lookup");
            await Expect(403, () => service.SetStatusAsync(actor, 1, new(), default), "Status rejects unauthorized user");
        }
        var created = await service.CreateAsync(admin.Id, new() { Name = "  Thuốc DEMO  ", Unit = " viên ", Price = 10, Description = " Mô tả " }, default);
        Check(created.Name == "Thuốc DEMO" && created.Unit == "viên" && created.IsActive && created.StockQuantity == 0, "Create normalizes fields without fabricating stock");
        Check((await service.GetAsync(admin.Id, created.Id, default)).Version == created.Version, "Version stable across SQL decimal-scale round trip");
        Check(await db.Set<AuditLog>().CountAsync(a => a.Entity == "Medicine" && a.Action == "Create") == 1, "Create audit saved");
        await Expect(409, () => service.CreateAsync(admin.Id, new() { Name = "thuốc demo", Unit = "VIÊN", Price = 10 }, default), "Duplicate normalized name and unit rejected");
        foreach (var invalid in new[] { new SaveMedicineRequest { Name = " ", Unit = "viên" }, new() { Name = "A", Unit = " " }, new() { Name = "A", Unit = "viên", Price = -1 }, new() { Name = "A", Unit = "viên", Price = 0.001m }, new() { Name = new string('x', 256), Unit = "viên" }, new() { Name = "A", Unit = "viên", Description = new string('x', 501) } })
            await Expect(400, () => service.CreateAsync(admin.Id, invalid, default), "Invalid fields rejected by service");
        var updated = await service.UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = created.Unit!, Price = 12.25m, ExpectedVersion = created.Version }, default);
        Check(updated.Price == 12.25m && updated.Version != created.Version, "Edit changes catalog price and version");
        await Expect(409, () => service.UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = created.Unit!, Price = 99, ExpectedVersion = created.Version }, default), "Stale edit cannot overwrite new price");
        var stopped = await service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = false, ExpectedVersion = updated.Version }, default);
        Check(!stopped.IsActive && await db.Set<Medicine>().CountAsync() == 1, "Deactivate retains medicine rather than delete");
        await Expect(409, () => service.CreateAsync(admin.Id, new() { Name = created.Name, Unit = created.Unit!, Price = 10 }, default), "Inactive duplicate requires reuse rather than new entry");
        var enabled = await service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = true, ExpectedVersion = stopped.Version }, default);
        var beforeReplay = await db.Set<AuditLog>().CountAsync();
        await service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = true, ExpectedVersion = enabled.Version }, default);
        Check(await db.Set<AuditLog>().CountAsync() == beforeReplay, "Same-state request does not duplicate audit");
        var doctor = new FoMed.Infrastructure.Models.Doctor { FullName = "Bác sĩ DEMO", IsActive = true,
            User = Actor("Doctor"), Specialty = new Specialty { Name = "Chuyên khoa DEMO", IsActive = true } };
        var patient = new Patient { PatientCode = "BN-MED-DEMO", FullName = "Bệnh nhân DEMO", IsActive = true };
        var appointment = new Appointment { AppointmentCode = "LH-MED-DEMO", Patient = patient, Doctor = doctor, Status = 3, StartTime = DateTime.Today, EndTime = DateTime.Today.AddMinutes(30), FeeSnapshot = 20 };
        var record = new MedicalRecord { Appointment = appointment, Doctor = doctor, Patient = patient, IsFinalized = true, CreatedAt = DateTime.UtcNow };
        record.Prescription = new Prescription { CreatedAt = DateTime.UtcNow, PrescriptionItems = [new PrescriptionItem { MedicineId = created.Id, Quantity = 2, UnitPriceSnapshot = 0 }] };
        db.Add(record); await db.SaveChangesAsync();
        await Expect(409, () => service.UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = "hộp", Price = 12.25m, ExpectedVersion = enabled.Version }, default), "Unit cannot reinterpret quantities on existing prescription");
        await Expect(409, () => service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = false, ExpectedVersion = enabled.Version }, default), "Pending unfilled prescription prevents deactivation");
        Check((await service.GetAsync(admin.Id, created.Id, default)).IsActive, "Rejected status preserves active medicine");
        var repriced = await service.UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = created.Unit!, Price = 50, ExpectedVersion = enabled.Version }, default);
        var item = record.Prescription.PrescriptionItems.Single();
        Check(item.UnitPriceSnapshot == 0 && item.Quantity == 2, "Repricing leaves prescription snapshot and quantity untouched");
        await db.Database.ExecuteSqlRawAsync("IF OBJECT_ID('billing.seq_invoice_no', 'SO') IS NULL EXEC('CREATE SEQUENCE billing.seq_invoice_no AS INT START WITH 1 INCREMENT BY 1')");
        var repo = new ClinicRepository(db);
        var billing = new BillingService(repo, new ClinicAccess(repo));
        Check((await billing.ListEligibleAsync(admin.Id, 1, default)).Single().EstimatedTotalAmount == 20, "Invoice estimate preserves zero-price prescription snapshot");
        var invoice = await billing.CreateAsync(admin.Id, new() { MedicalRecordId = record.Id }, default);
        Check(invoice.TotalAmount == 20, "Zero-price prescription remains free when invoice created after catalog repricing");
        await service.UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = created.Unit!, Price = 75, ExpectedVersion = repriced.Version }, default);
        Check((await billing.GetAsync(admin.Id, invoice.Id, default)).TotalAmount == 20, "Existing invoice price unchanged by catalog edit");
        var batch = new MedicineBatch { MedicineId = created.Id, LotNumber = "DEMO", Quantity = 10, ExpiryDate = DateOnly.FromDateTime(DateTime.Today.AddYears(1)) };
        db.Add(batch); await db.SaveChangesAsync();
        db.Add(new PrescriptionDispense { PrescriptionItemId = item.Id, BatchId = batch.Id, Quantity = 1, DispensedBy = admin.Id, DispensedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        var current = await service.GetAsync(admin.Id, created.Id, default);
        await Expect(409, () => service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = false, ExpectedVersion = current.Version }, default), "Partially dispensed prescription still blocks deactivation");
        db.Add(new PrescriptionDispense { PrescriptionItemId = item.Id, BatchId = batch.Id, Quantity = 1, DispensedBy = admin.Id, DispensedAt = DateTime.UtcNow }); await db.SaveChangesAsync();
        var done = await service.SetStatusAsync(admin.Id, created.Id, new() { IsActive = false, ExpectedVersion = current.Version }, default);
        Check(done.StockQuantity == 10 && done.AvailableQuantity == 0 && !done.IsActive, "Fully dispensed prescription allows stop while retaining stock and history");
        Check(await db.Set<PrescriptionDispense>().CountAsync() == 2 && await db.Set<Invoice>().CountAsync() == 1, "Status retains dispensing and invoice records");
        Check(await db.Set<AuditLog>().AnyAsync(a => a.Entity == "Medicine" && a.Action == "Update" && a.OldValue != null && a.NewValue != null), "Edit audit contains before/after catalog fields");
        async Task<int> ConcurrentEdit(decimal price)
        {
            await using var concurrentDb = new FoMedDbContext(options);
            try { await Service(concurrentDb).UpdateAsync(admin.Id, created.Id, new() { Name = created.Name, Unit = created.Unit!, Price = price, ExpectedVersion = done.Version }, default); return 200; }
            catch (ClinicException e) { return e.StatusCode; }
        }
        var outcomes = await Task.WhenAll(ConcurrentEdit(90), ConcurrentEdit(100));
        Check(outcomes.Count(code => code == 200) == 1 && outcomes.Count(code => code == 409) == 1, "Concurrent SQL edits allow one writer and reject the stale writer");
    }
    finally
    {
        // Exact randomly generated target only; never drop an application database.
        await using var cleanup = new FoMedDbContext(options);
        await cleanup.Database.EnsureDeletedAsync();
        Console.WriteLine("Removed isolated local test database " + name);
    }
}
Console.WriteLine($"Medicine catalog: {checks} checks passed. {(args.Contains("--sql") ? "Isolated local SQL integration included." : "Offline tests only; use --sql for local write integration.")}");

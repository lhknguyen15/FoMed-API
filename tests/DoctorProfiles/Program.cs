using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Doctor;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

// dotnet run --project tests/DoctorProfiles [-- --database]
var assertions = 0;
void Check(bool result, string message)
{
    if (!result) throw new InvalidOperationException(message);
    assertions++;
}

var entity = new Doctor
{
    Id = 11, UserId = 22, SpecialtyId = 33, FullName = "Bác sĩ kiểm thử", Title = "BS",
    IsActive = true, Specialty = new Specialty { Id = 33, Name = "Chuyên khoa", IsActive = true },
    Room = "P101", ConsultationFee = 150000, Phone = "PRIVATE_PHONE", LicenseNumber = "PRIVATE_LICENSE",
    Biography = "Giới thiệu", AvatarUrl = "https://example.com/doctor.png", PracticeStartYear = 2010,
};
var saves = 0;
Doctor? returned = entity;
var doctorRepository = Stub.Create<IDoctorRepository>((name, _) => name switch
{
    "GetActiveByIdAsync" or "GetByUserIdAsync" => Task.FromResult(returned),
    "GetActiveAsync" => Task.FromResult<IReadOnlyList<Doctor>>([entity]),
    "Update" => null,
    _ => throw new InvalidOperationException(name),
});
var uow = Stub.Create<IUnitOfWork>((name, _) => name switch
{
    "get_DoctorRepository" => doctorRepository,
    "get_SpecialtyRepository" => Stub.Create<ISpecialtyRepository>((_, _) => Task.FromResult<Specialty?>(entity.Specialty)),
    "SaveChangesAsync" => Task.FromResult(++saves),
    _ => throw new InvalidOperationException(name),
});
var service = new DoctorService(uow);
var publicList = await service.GetPublicDoctorsAsync(null, null, default);
Check(publicList.DataResponse.Single().AvatarUrl == entity.AvatarUrl, "Public doctor list avatar missing");
Check(!JsonSerializer.Serialize(publicList.DataResponse).Contains("PRIVATE_"), "Private fields leaked in public list");
var detail = await service.GetPublicDoctorAsync(11, default);
Check(detail.StatusCode == 200 && detail.DataResponse?.Biography == entity.Biography, "Public profile fields were not mapped");
Check(detail.DataResponse?.Room == "P101" && detail.DataResponse.PracticeStartYear == 2010, "Room/year missing");
var serialized = JsonSerializer.Serialize(detail.DataResponse);
Check(!serialized.Contains("PRIVATE_") && !serialized.Contains("UserId"), "Private fields leaked");
Check(!typeof(PublicDoctorDetailResponse).GetProperties().Any(p => new[] { "Phone", "LicenseNumber", "UserId", "IsActive" }.Contains(p.Name)), "Public DTO includes private fields");
returned = null;
Check((await service.GetPublicDoctorAsync(999, default)).StatusCode == 404, "Missing doctor was exposed");
returned = entity;
entity.IsActive = false;
Check((await service.GetPublicDoctorAsync(11, default)).StatusCode == 404, "Inactive doctor was exposed");
entity.IsActive = true;
entity.Specialty.IsActive = false;
Check((await service.GetPublicDoctorAsync(11, default)).StatusCode == 404, "Inactive specialty was exposed");
entity.Specialty.IsActive = true;

var request = new UpdateDoctorProfileRequest
{
    FullName = entity.FullName, SpecialtyId = entity.SpecialtyId, ConsultationFee = entity.ConsultationFee,
    Biography = "  Hồ sơ mới  ", AvatarUrl = " https://example.com/new.png ", PracticeStartYear = 2015,
};
var updated = await service.UpdateMyProfileAsync(22, request, default);
Check(saves == 1 && updated.DataResponse?.Biography == "Hồ sơ mới", "Profile update was not saved/normalized");
Check(updated.DataResponse?.AvatarUrl == "https://example.com/new.png" && updated.DataResponse.PracticeStartYear == 2015, "New profile fields were not saved");
await service.UpdateMyProfileAsync(22, request with { AvatarUrl = null, Biography = " ", PracticeStartYear = null }, default);
Check(entity.AvatarUrl == null && entity.Biography == null && entity.PracticeStartYear == null, "Optional fields cannot be cleared");

bool Valid(object input) => Validator.TryValidateObject(input, new ValidationContext(input), [], true);
Check(Valid(request), "Valid profile rejected");
Check(!Valid(request with { AvatarUrl = "javascript:alert(1)" }), "Unsafe avatar URL accepted");
Check(!Valid(request with { PracticeStartYear = DateTime.UtcNow.Year + 1 }), "Future practice year accepted");
Check(!Valid(request with { Biography = new string('x', 5001) }), "Oversized biography accepted");
Check(!Valid(new CreateDoctorRequest { Username = "test", Password = "Test12345", FullName = "Test", SpecialtyId = 1, AvatarUrl = "file:///secret" }), "Admin create skips public profile validation");
Check(!Valid(new UpdateDoctorAdminRequest { FullName = "Test", SpecialtyId = 1, PracticeStartYear = DateTime.UtcNow.Year + 1 }), "Admin update skips year validation");

if (args.Contains("--database"))
{
    using var config = JsonDocument.Parse(File.ReadAllText("FoMed-API/FoMed.Api/appsettings.Development.json"));
    var connection = config.RootElement.GetProperty("ConnectionStrings").GetProperty("DefaultConnection").GetString()!;
    await using var db = new FoMedDbContext(new DbContextOptionsBuilder<FoMedDbContext>().UseSqlServer(connection).Options);
    var activeId = await db.Doctors.Where(d => d.IsActive && d.Specialty.IsActive).Select(d => (int?)d.Id).FirstOrDefaultAsync();
    Check(activeId.HasValue, "No active doctor available for the database smoke test");
    var repo = new DoctorRepository(db);
    var original = await repo.GetActiveByIdAsync(activeId!.Value) ?? throw new InvalidOperationException("Doctor missing");
    var databaseList = await new DoctorService(Stub.Create<IUnitOfWork>((name, _) => name switch
    {
        "get_DoctorRepository" => repo,
        _ => throw new InvalidOperationException(name),
    })).GetPublicDoctorsAsync(null, null, default);
    Check(databaseList.DataResponse.Single(d => d.DoctorId == activeId.Value).AvatarUrl == original.AvatarUrl,
        "SQL-backed public list avatar differs from the stored profile");
    await using var transaction = await db.Database.BeginTransactionAsync();
    var databaseService = new DoctorService(Stub.Create<IUnitOfWork>((name, parameters) => name switch
    {
        "get_DoctorRepository" => repo,
        "get_SpecialtyRepository" => new SpecialtyRepository(db),
        "SaveChangesAsync" => db.SaveChangesAsync((CancellationToken)parameters![0]!),
        _ => throw new InvalidOperationException(name),
    }));
    try
    {
        var result = await databaseService.UpdateMyProfileAsync(original.UserId, new UpdateDoctorProfileRequest
        {
            FullName = original.FullName, SpecialtyId = original.SpecialtyId, Title = original.Title,
            LicenseNumber = original.LicenseNumber, Phone = original.Phone, Room = original.Room,
            ConsultationFee = original.ConsultationFee, Biography = "Kiểm thử hồ sơ — rollback",
            AvatarUrl = "https://example.com/profile-test.png", PracticeStartYear = 2012,
        }, default);
        Check(result.StatusCode == 200, "SQL-backed update failed");
        db.ChangeTracker.Clear();
        var readBack = await databaseService.GetPublicDoctorAsync(activeId.Value, default);
        Check(readBack.DataResponse?.Biography == "Kiểm thử hồ sơ — rollback" && readBack.DataResponse.PracticeStartYear == 2012, "SQL round-trip lost profile fields");
    }
    finally { await transaction.RollbackAsync(); }
    db.ChangeTracker.Clear();
    var restored = await repo.GetActiveByIdAsync(activeId.Value);
    Check(restored?.Biography == original.Biography && restored?.AvatarUrl == original.AvatarUrl && restored?.PracticeStartYear == original.PracticeStartYear, "Database test did not restore original profile");
}

Console.WriteLine($"Doctor profile checks passed: {assertions}");

public class Stub : DispatchProxy
{
    private Func<string, object?[]?, object?> handler = null!;
    public static T Create<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, Stub>();
        ((Stub)(object)proxy).handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!.Name, args);
}

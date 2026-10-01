using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IPatientRepository : IRepositoryBase<Patient>
{
    Task<string> GeneratePatientCodeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Patient>> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default);
    // Lấy hồ sơ bệnh nhân gắn với tài khoản đang đăng nhập.
    Task<Patient?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed class PatientRepository(FoMedDbContext dbContext)
    : RepositoryBase<Patient>(dbContext), IPatientRepository
{
    private readonly FoMedDbContext dbContext = dbContext;

    public async Task<string> GeneratePatientCodeAsync(CancellationToken cancellationToken = default)
    {
        // Materialize directly: NEXT VALUE FOR cannot be composed inside a SQL subquery.
        var values = await dbContext.Database
            .SqlQueryRaw<int>("SELECT NEXT VALUE FOR scheduling.seq_patient_code AS Value")
            .ToListAsync(cancellationToken);
        return "BN" + values.Single().ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    // UserId là khóa liên kết giữa tài khoản auth.users và hồ sơ scheduling.patients.
    public Task<Patient?> GetByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Patients
            .SingleOrDefaultAsync(patient => patient.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Patient>> FindByPhoneAsync(
        string phone, CancellationToken cancellationToken = default) =>
        await dbContext.Patients
            .Where(patient => patient.Phone == phone)
            .OrderBy(patient => patient.Id)
            .ToListAsync(cancellationToken);
}

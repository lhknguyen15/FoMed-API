using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface ISpecialtyRepository : IRepositoryBase<Specialty>
{
    // Lấy chuyên khoa đang hoạt động để kiểm tra dữ liệu Doctor.
    Task<Specialty?> GetActiveByIdAsync(
        int specialtyId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Specialty>> GetActiveAsync(
        CancellationToken cancellationToken = default);
}

public sealed class SpecialtyRepository(FoMedDbContext dbContext)
    : RepositoryBase<Specialty>(dbContext), ISpecialtyRepository
{
    private readonly FoMedDbContext dbContext = dbContext;

    public Task<Specialty?> GetActiveByIdAsync(
        int specialtyId,
        CancellationToken cancellationToken = default) =>
        dbContext.Specialties
            .AsNoTracking()
            .SingleOrDefaultAsync(
                specialty => specialty.Id == specialtyId && specialty.IsActive,
                cancellationToken);

    public async Task<IReadOnlyList<Specialty>> GetActiveAsync(
        CancellationToken cancellationToken = default) =>
        await dbContext.Specialties
            .AsNoTracking()
            .Where(specialty => specialty.IsActive)
            .OrderBy(specialty => specialty.Name)
            .ToListAsync(cancellationToken);
}

using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IDoctorRepository : IRepositoryBase<Doctor>
{
    Task<Doctor?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default);
    // Lấy hồ sơ bác sĩ gắn với tài khoản đang đăng nhập.
    Task<Doctor?> GetByUserIdAsync(int userId, CancellationToken cancellationToken = default);

    // Lấy danh sách bác sĩ đang hoạt động kèm thông tin chuyên khoa.
    Task<IReadOnlyList<Doctor>> GetActiveAsync(
        int? specialtyId,
        string? search,
        CancellationToken cancellationToken = default);
}

public sealed class DoctorRepository(FoMedDbContext dbContext)
    : RepositoryBase<Doctor>(dbContext), IDoctorRepository
{
    private readonly FoMedDbContext dbContext = dbContext;

    public Task<Doctor?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.Doctors.AsNoTracking().Include(doctor => doctor.Specialty)
            .SingleOrDefaultAsync(doctor => doctor.Id == id && doctor.IsActive && doctor.Specialty.IsActive,
                cancellationToken);

    public Task<Doctor?> GetByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default) =>
        dbContext.Doctors
            .Include(doctor => doctor.Specialty)
            .SingleOrDefaultAsync(doctor => doctor.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Doctor>> GetActiveAsync(
        int? specialtyId,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Doctors
            .AsNoTracking()
            .Include(doctor => doctor.Specialty)
            .Where(doctor => doctor.IsActive && doctor.Specialty.IsActive);

        if (specialtyId.HasValue)
        {
            query = query.Where(doctor => doctor.SpecialtyId == specialtyId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchTerm = search.Trim();
            query = query.Where(doctor =>
                doctor.FullName.Contains(searchTerm) ||
                (doctor.Title != null && doctor.Title.Contains(searchTerm)) ||
                doctor.Specialty.Name.Contains(searchTerm));
        }

        return await query
            .OrderBy(doctor => doctor.FullName)
            .ToListAsync(cancellationToken);
    }
}

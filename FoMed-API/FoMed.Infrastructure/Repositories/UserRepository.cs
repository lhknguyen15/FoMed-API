using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IUserRepository : IRepositoryBase<User>
{
    Task<Role?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default);
    // Lấy user theo email để check login/register.
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    // Lấy user theo id kèm role để phục vụ profile và authorization.
    Task<User?> GetByIdWithRolesAsync(int userId, CancellationToken cancellationToken = default);
}

public sealed class UserRepository : RepositoryBase<User>, IUserRepository
{
    private readonly FoMedDbContext dbContext;

    public UserRepository(FoMedDbContext dbContext) : base(dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken cancellationToken = default) =>
        dbContext.Roles.SingleOrDefaultAsync(role => role.Name == name, cancellationToken);

    // Load user + role để tạo token hoặc kiểm tra quyền.
    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    // Load user theo id và include role để profile API có thể lấy role hiện tại.
    public Task<User?> GetByIdWithRolesAsync(int userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
}

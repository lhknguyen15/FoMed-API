using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IUserRepository : IRepositoryBase<User>
{
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);
}

public sealed class UserRepository : RepositoryBase<User>, IUserRepository
{
    private readonly FoMedDbContext dbContext;

    public UserRepository(FoMedDbContext dbContext) : base(dbContext)
    {
        this.dbContext = dbContext;
    }

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Include(user => user.UserRoles)
            .ThenInclude(userRole => userRole.Role)
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);
}
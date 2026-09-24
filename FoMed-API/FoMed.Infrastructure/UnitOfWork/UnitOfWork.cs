using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Repositories;

namespace FoMed.Infrastructure.UnitOfWork;

public interface IUnitOfWork
{
    IUserRepository UserRepository { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class UnitOfWork(FoMedDbContext dbContext, IUserRepository users) : IUnitOfWork
{
    public IUserRepository UserRepository { get; } = users;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
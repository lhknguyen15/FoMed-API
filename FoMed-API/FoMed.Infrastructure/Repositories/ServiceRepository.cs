using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public interface IServiceRepository
{
    Task<Service?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class ServiceRepository(FoMedDbContext dbContext) : IServiceRepository
{
    public Task<Service?> GetActiveByIdAsync(int id, CancellationToken cancellationToken = default) =>
        dbContext.Services
            .AsNoTracking()
            .SingleOrDefaultAsync(service => service.Id == id && service.IsActive, cancellationToken);
}

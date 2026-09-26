using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using FoMed.Infrastructure.DbContext;

namespace FoMed.Infrastructure.UnitOfWork;

public interface IWriteScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
}

// A database-owned lock works across API processes, unlike an in-memory semaphore.
public sealed class WriteScope(IDbContextTransaction transaction) : IWriteScope
{
    public Task CommitAsync(CancellationToken cancellationToken = default) => transaction.CommitAsync(cancellationToken);
    public ValueTask DisposeAsync() => transaction.DisposeAsync();

    public static async Task<IWriteScope> BeginAsync(FoMedDbContext db, CancellationToken ct)
    {
        var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource = 'FoMed:clinic-workflow',
                    @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
                IF @result < 0 THROW 51001, 'Clinic workflow is busy. Retry the request.', 1;
                """, ct);
            return new WriteScope(transaction);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }
}

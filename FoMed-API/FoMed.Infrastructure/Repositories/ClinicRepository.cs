using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Infrastructure.Repositories;

public sealed class ClinicRepository(FoMedDbContext db)
{
    public IQueryable<T> Query<T>() where T : class => db.Set<T>();
    public void Add<T>(T entity) where T : class => db.Set<T>().Add(entity);
    public void Remove<T>(T entity) where T : class => db.Set<T>().Remove(entity);
    public Task<int> SaveAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
    public Task<IWriteScope> BeginWriteAsync(CancellationToken ct) => WriteScope.BeginAsync(db, ct);
    public async Task<string> NextInvoiceNumberAsync(CancellationToken ct)
    {
        var values = await db.Database.SqlQueryRaw<int>("SELECT NEXT VALUE FOR billing.seq_invoice_no AS Value").ToListAsync(ct);
        return "HD" + values.Single().ToString("D10", System.Globalization.CultureInfo.InvariantCulture);
    }
    public async Task<string> NextPatientCodeAsync(CancellationToken ct)
    {
        var values = await db.Database.SqlQueryRaw<int>("SELECT NEXT VALUE FOR scheduling.seq_patient_code AS Value").ToListAsync(ct);
        return "BN" + values.Single().ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}

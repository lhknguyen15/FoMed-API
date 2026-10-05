using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Clinical;

internal static class PrescriptionDispensingState
{
    // Partial dispensing locks editing, including old batch/OUT allocations.
    public static Task<bool> HasDispensedAsync(ClinicRepository repository, int prescriptionId, CancellationToken ct) =>
        repository.Query<PrescriptionItem>().AnyAsync(item => item.PrescriptionId == prescriptionId &&
            (item.PrescriptionDispenses.Any() || (item.BatchId.HasValue && repository.Query<StockTransaction>()
                .Any(t => t.BatchId == item.BatchId && t.RefId == prescriptionId && t.Type == 1 && t.Quantity < 0))), ct);
}

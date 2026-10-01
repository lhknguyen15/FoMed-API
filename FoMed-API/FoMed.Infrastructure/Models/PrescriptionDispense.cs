namespace FoMed.Infrastructure.Models;

public partial class PrescriptionDispense
{
    public long Id { get; set; }
    public int PrescriptionItemId { get; set; }
    public int BatchId { get; set; }
    public int Quantity { get; set; }
    public int DispensedBy { get; set; }
    public DateTime DispensedAt { get; set; }

    public virtual PrescriptionItem PrescriptionItem { get; set; } = null!;
    public virtual MedicineBatch Batch { get; set; } = null!;
    public virtual User DispensedByNavigation { get; set; } = null!;
}

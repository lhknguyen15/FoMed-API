namespace FoMed.Infrastructure.Models;

public partial class InventoryReceiptItem
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int MedicineId { get; set; }
    public int BatchId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineAmount { get; set; }

    public virtual InventoryReceipt Receipt { get; set; } = null!;
    public virtual Medicine Medicine { get; set; } = null!;
    public virtual MedicineBatch Batch { get; set; } = null!;
}

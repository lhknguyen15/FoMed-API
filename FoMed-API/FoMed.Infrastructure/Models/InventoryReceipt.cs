namespace FoMed.Infrastructure.Models;

public partial class InventoryReceipt
{
    public int Id { get; set; }
    public string SupplierName { get; set; } = null!;
    public string DocumentNo { get; set; } = null!;
    public int ReceivedBy { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? Note { get; set; }

    public virtual User ReceivedByNavigation { get; set; } = null!;
    public virtual ICollection<InventoryReceiptItem> Items { get; set; } = new List<InventoryReceiptItem>();
}

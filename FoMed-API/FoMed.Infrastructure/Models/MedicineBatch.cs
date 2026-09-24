using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class MedicineBatch
{
    public int Id { get; set; }

    public int MedicineId { get; set; }

    public string LotNumber { get; set; } = null!;

    public DateOnly ExpiryDate { get; set; }

    public int Quantity { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();

    public virtual ICollection<StockTransaction> StockTransactions { get; set; } = new List<StockTransaction>();
}

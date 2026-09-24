using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class StockTransaction
{
    public long Id { get; set; }

    public int BatchId { get; set; }

    public byte Type { get; set; }

    public int Quantity { get; set; }

    public string? RefType { get; set; }

    public int? RefId { get; set; }

    public int CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual MedicineBatch Batch { get; set; } = null!;

    public virtual User CreatedByNavigation { get; set; } = null!;
}

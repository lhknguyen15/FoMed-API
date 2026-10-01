using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class PrescriptionItem
{
    public int Id { get; set; }

    public int PrescriptionId { get; set; }

    public int MedicineId { get; set; }

    public int? BatchId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPriceSnapshot { get; set; }

    public string? Dosage { get; set; }

    public string? Instruction { get; set; }

    public virtual MedicineBatch? Batch { get; set; }

    public virtual Medicine Medicine { get; set; } = null!;

    public virtual Prescription Prescription { get; set; } = null!;

    public virtual ICollection<PrescriptionDispense> PrescriptionDispenses { get; set; } = new List<PrescriptionDispense>();
}

using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Prescription
{
    public int Id { get; set; }

    public int MedicalRecordId { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual MedicalRecord MedicalRecord { get; set; } = null!;

    public virtual ICollection<PrescriptionItem> PrescriptionItems { get; set; } = new List<PrescriptionItem>();
}

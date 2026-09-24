using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class MedicalRecordService
{
    public int Id { get; set; }

    public int MedicalRecordId { get; set; }

    public int ServiceId { get; set; }

    public byte Status { get; set; }

    public int OrderedBy { get; set; }

    public DateTime OrderedAt { get; set; }

    public virtual LabResult? LabResult { get; set; }

    public virtual MedicalRecord MedicalRecord { get; set; } = null!;

    public virtual Doctor OrderedByNavigation { get; set; } = null!;

    public virtual Service Service { get; set; } = null!;
}

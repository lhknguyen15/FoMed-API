using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class LabResult
{
    public int Id { get; set; }

    public int MedicalRecordServiceId { get; set; }

    public string? ResultSummary { get; set; }

    public string? Conclusion { get; set; }

    public int TechnicianId { get; set; }

    public DateTime ResultAt { get; set; }

    public virtual MedicalRecordService MedicalRecordService { get; set; } = null!;

    public virtual User Technician { get; set; } = null!;
}

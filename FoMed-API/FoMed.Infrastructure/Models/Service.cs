using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Service
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Code { get; set; }

    public string? Description { get; set; }

    public decimal Price { get; set; }

    public int? SpecialtyId { get; set; }

    public int DurationMinutes { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    public virtual ICollection<MedicalRecordService> MedicalRecordServices { get; set; } = new List<MedicalRecordService>();

    public virtual Specialty? Specialty { get; set; }
}

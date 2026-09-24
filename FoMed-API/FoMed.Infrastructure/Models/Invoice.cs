using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Invoice
{
    public int Id { get; set; }

    public string InvoiceNo { get; set; } = null!;

    public int PatientId { get; set; }

    public int? AppointmentId { get; set; }

    public int? MedicalRecordId { get; set; }

    public string? PatientName { get; set; }

    public decimal TotalAmount { get; set; }

    public byte Status { get; set; }

    public int? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Appointment? Appointment { get; set; }

    public virtual User? CreatedByNavigation { get; set; }

    public virtual ICollection<InvoiceItem> InvoiceItems { get; set; } = new List<InvoiceItem>();

    public virtual MedicalRecord? MedicalRecord { get; set; }

    public virtual Patient Patient { get; set; } = null!;

    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}

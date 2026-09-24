using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class MedicalRecord
{
    public int Id { get; set; }

    public int AppointmentId { get; set; }

    public int PatientId { get; set; }

    public int DoctorId { get; set; }

    public string? Symptoms { get; set; }

    public string? Diagnosis { get; set; }

    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public virtual Appointment Appointment { get; set; } = null!;

    public virtual Doctor Doctor { get; set; } = null!;

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<MedicalRecordService> MedicalRecordServices { get; set; } = new List<MedicalRecordService>();

    public virtual Patient Patient { get; set; } = null!;

    public virtual Prescription? Prescription { get; set; }
}

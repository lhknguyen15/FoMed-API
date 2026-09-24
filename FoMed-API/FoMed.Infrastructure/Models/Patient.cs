using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Patient
{
    public int Id { get; set; }

    public string PatientCode { get; set; } = null!;

    public int? UserId { get; set; }

    public string FullName { get; set; } = null!;

    public byte? Gender { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual User? User { get; set; }
}

using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Doctor
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int SpecialtyId { get; set; }

    public string FullName { get; set; } = null!;

    public string? Title { get; set; }

    public string? LicenseNumber { get; set; }

    public string? Phone { get; set; }

    public string? Room { get; set; }

    public decimal ConsultationFee { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();

    public virtual ICollection<DoctorSchedule> DoctorSchedules { get; set; } = new List<DoctorSchedule>();

    public virtual ICollection<DoctorTimeOff> DoctorTimeOffs { get; set; } = new List<DoctorTimeOff>();

    public virtual ICollection<MedicalRecordService> MedicalRecordServices { get; set; } = new List<MedicalRecordService>();

    public virtual ICollection<MedicalRecord> MedicalRecords { get; set; } = new List<MedicalRecord>();

    public virtual Specialty Specialty { get; set; } = null!;

    public virtual User User { get; set; } = null!;
}

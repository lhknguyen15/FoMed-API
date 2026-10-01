using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Patient;

public sealed record PatientResponse(
    int PatientId,
    int? UserId,
    string PatientCode,
    string FullName,
    byte? Gender,
    DateOnly? DateOfBirth,
    string? Phone,
    string? Address,
    string? NationalId,
    string? InsuranceNumber,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    string? Allergies,
    bool IsActive);

public sealed record UpdatePatientProfileRequest
{
    [Required]
    [MaxLength(255)]
    public string FullName { get; init; } = string.Empty;

    [Range(0, 2)]
    public byte? Gender { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    [Phone]
    [MaxLength(20)]
    public string? Phone { get; init; }

    [MaxLength(500)]
    public string? Address { get; init; }

    [MaxLength(20)]
    public string? NationalId { get; init; }

    [MaxLength(50)]
    public string? InsuranceNumber { get; init; }

    [MaxLength(255)]
    public string? EmergencyContactName { get; init; }

    [Phone, MaxLength(20)]
    public string? EmergencyContactPhone { get; init; }

    [MaxLength(1000)]
    public string? Allergies { get; init; }
}

public sealed record PatientStaffSearchRequest(
    string? Phone = null,
    string? Name = null,
    string? PatientCode = null,
    int Page = 1,
    int PageSize = 20);

public sealed record CreateWalkInPatientRequest
{
    [Required, MaxLength(255)] public string FullName { get; init; } = string.Empty;
    [Required, Phone, MaxLength(20)] public string Phone { get; init; } = string.Empty;
    public byte? Gender { get; init; }
    public DateOnly? DateOfBirth { get; init; }
    [MaxLength(500)] public string? Address { get; init; }
    [MaxLength(20)] public string? NationalId { get; init; }
    [MaxLength(50)] public string? InsuranceNumber { get; init; }
    [MaxLength(255)] public string? EmergencyContactName { get; init; }
    [Phone, MaxLength(20)] public string? EmergencyContactPhone { get; init; }
    [MaxLength(1000)] public string? Allergies { get; init; }
}

public sealed record PatientHistoryResponse(
    int AppointmentId,
    string AppointmentCode,
    DateTime StartTime,
    byte Status,
    string StatusName,
    int DoctorId,
    string DoctorName,
    int? ServiceId,
    string? ServiceName,
    int? MedicalRecordId,
    string? Diagnosis);

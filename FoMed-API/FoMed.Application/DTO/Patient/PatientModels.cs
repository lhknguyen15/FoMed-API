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
}

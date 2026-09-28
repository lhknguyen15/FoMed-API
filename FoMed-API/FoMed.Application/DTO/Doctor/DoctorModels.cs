using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Doctor;

public sealed record DoctorResponse(
    int DoctorId,
    int UserId,
    int SpecialtyId,
    string SpecialtyName,
    string FullName,
    string? Title,
    string? LicenseNumber,
    string? Phone,
    string? Room,
    decimal ConsultationFee,
    bool IsActive);

public sealed record PublicDoctorResponse(
    int DoctorId,
    int SpecialtyId,
    string SpecialtyName,
    string FullName,
    string? Title,
    decimal ConsultationFee);

public sealed record UpdateDoctorProfileRequest
{
    [Required]
    [MaxLength(255)]
    public string FullName { get; init; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int SpecialtyId { get; init; }

    [MaxLength(100)]
    public string? Title { get; init; }

    [MaxLength(100)]
    public string? LicenseNumber { get; init; }

    [Phone]
    [MaxLength(20)]
    public string? Phone { get; init; }

    [MaxLength(50)]
    public string? Room { get; init; }

    [Range(typeof(decimal), "0", "9999999999")]
    public decimal ConsultationFee { get; init; }
}

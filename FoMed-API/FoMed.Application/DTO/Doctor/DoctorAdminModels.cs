using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Doctor;

public sealed record CreateDoctorRequest : DoctorPublicProfileRequest
{
    [Required, MaxLength(100)] public string Username { get; init; } = "";
    [Required, MinLength(8), MaxLength(100)] public string Password { get; init; } = "";
    [EmailAddress, MaxLength(255)] public string? Email { get; init; }
    [Required, MaxLength(255)] public string FullName { get; init; } = "";
    [Range(1, int.MaxValue)] public int SpecialtyId { get; init; }
    [MaxLength(100)] public string? Title { get; init; }
    [MaxLength(100)] public string? LicenseNumber { get; init; }
    [Phone, MaxLength(20)] public string? Phone { get; init; }
    [MaxLength(50)] public string? Room { get; init; }
    [Range(typeof(decimal), "0", "9999999999")] public decimal ConsultationFee { get; init; }
}

public sealed record UpdateDoctorAdminRequest : DoctorPublicProfileRequest
{
    [Required, MaxLength(255)] public string FullName { get; init; } = "";
    [Range(1, int.MaxValue)] public int SpecialtyId { get; init; }
    [MaxLength(100)] public string? Title { get; init; }
    [MaxLength(100)] public string? LicenseNumber { get; init; }
    [Phone, MaxLength(20)] public string? Phone { get; init; }
    [MaxLength(50)] public string? Room { get; init; }
    [Range(typeof(decimal), "0", "9999999999")] public decimal ConsultationFee { get; init; }
    public bool? IsActive { get; init; }
}

public sealed record CreateSpecialtyRequest
{
    [Required, MaxLength(255)] public string Name { get; init; } = "";
    [MaxLength(500)] public string? Description { get; init; }
}

public sealed record UpdateSpecialtyRequest
{
    [Required, MaxLength(255)] public string Name { get; init; } = "";
    [MaxLength(500)] public string? Description { get; init; }
    public bool IsActive { get; init; }
}

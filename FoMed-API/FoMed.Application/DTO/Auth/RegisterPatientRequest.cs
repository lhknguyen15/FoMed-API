using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Auth;

public sealed record RegisterPatientRequest
{
    [Required, MaxLength(150)]
    public required string FullName { get; init; }

    [Required, Phone, MaxLength(20)]
    public required string Phone { get; init; }

    [EmailAddress, MaxLength(255)]
    public string? Email { get; init; }

    public DateTime? DateOfBirth { get; init; }

    [Required, MinLength(8), MaxLength(100)]
    public required string Password { get; init; }
}

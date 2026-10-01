using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Auth;

public sealed record RefreshTokenRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

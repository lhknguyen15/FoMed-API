using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Auth;

// Request theo Review ERD: POST /api/auth/forgot-password.
public sealed record ForgotPasswordRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }
}

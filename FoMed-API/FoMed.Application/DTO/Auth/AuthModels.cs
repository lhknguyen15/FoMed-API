using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Auth;

public sealed record RegisterRequest
{
    [Required, MaxLength(150)]
    public required string FullName { get; init; }

    [Required, EmailAddress, MaxLength(256)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(100)]
    public required string Password { get; init; }
}

public sealed record LoginRequest
{
    [Required, EmailAddress]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}

public sealed record AuthResponse(int UserId, string FullName, string? Email, string Role, string AccessToken);
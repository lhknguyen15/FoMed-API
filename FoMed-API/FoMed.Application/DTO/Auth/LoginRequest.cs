using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FoMed.Application.DTO.Auth;

public sealed record LoginRequest
{
    private string? username;

    [Required, MaxLength(256)]
    public string Username { get => username ?? string.Empty; init => username = value; }

    // Giữ alias email cho các client cũ; hợp đồng chuẩn dùng username.
    [JsonPropertyName("email")]
    [EmailAddress, MaxLength(256)]
    public string? Email { get => null; init { if (string.IsNullOrWhiteSpace(username)) username = value; } }

    [Required]
    public required string Password { get; init; }
}

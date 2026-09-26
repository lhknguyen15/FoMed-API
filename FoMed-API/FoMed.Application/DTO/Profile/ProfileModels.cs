using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Profile;

public sealed record ChangePasswordRequest
{
    [Required(ErrorMessage = "Mật khẩu hiện tại là bắt buộc.")]
    public required string OldPassword { get; init; }

    [Required, MinLength(8), MaxLength(100)]
    public required string NewPassword { get; init; }

    [Required]
    [Compare(nameof(NewPassword), ErrorMessage = "Xác nhận mật khẩu không khớp.")]
    public required string ConfirmPassword { get; init; }
}

public sealed record ProfileResponse(
    int UserId,
    string FullName,
    string Email,
    string? Phone,
    string Role,
    bool IsActive);

public sealed record UpdateProfileRequest
{
    [MaxLength(150)]
    public string? FullName { get; init; }

    [Phone]
    [MaxLength(20)]
    public string? Phone { get; init; }
}

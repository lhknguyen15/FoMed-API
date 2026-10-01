using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Auth;

public sealed record AdminUserResponse(int UserId, string Username, string? Email, string? FullName, string? Phone, bool IsActive, DateTime CreatedAt, IReadOnlyList<string> Roles, int? DoctorId, int? PatientId);
public sealed record AdminRoleResponse(int RoleId, string Name, int UserCount);
public sealed record UpdateUserStatusRequest(bool IsActive);
public sealed record UpdateUserRolesRequest
{
    [Required, MinLength(1)] public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
public sealed record ResetUserPasswordRequest
{
    [Required, MinLength(8), MaxLength(100)] public string NewPassword { get; init; } = string.Empty;
}

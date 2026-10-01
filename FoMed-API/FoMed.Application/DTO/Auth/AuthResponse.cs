namespace FoMed.Application.DTO.Auth;

// Response theo Review ERD cho login và refresh.
public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresIn,
    AuthenticatedUserResponse User);

public sealed record AuthenticatedUserResponse(
    int Id,
    string FullName,
    string[] Roles,
    int? DoctorId,
    int? PatientId);

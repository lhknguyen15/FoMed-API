using FoMed.Application.DTO;
using FoMed.Application.DTO.Profile;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.UnitOfWork;
using FoMed.Infrastructure.Authentication;
using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.Services.Profile;

public sealed class ProfileService(IUnitOfWork unitOfWork, IPasswordHasher passwordHasher)
{
    public async Task<HTTPResponseData<string?>> ChangePasswordAsync(
        int userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            return new() { DataResponse = null, Message = errors[0].ErrorMessage!, StatusCode = 400 };

        var user = await unitOfWork.UserRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
            return new() { DataResponse = null, Message = ProfileResponseMessageDTO.ProfileNotFound, StatusCode = 404 };

        if (!passwordHasher.Verify(request.OldPassword, user.PasswordHash))
            return new() { DataResponse = null, Message = ProfileResponseMessageDTO.WrongOldPassword, StatusCode = 400 };

        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new() { DataResponse = null, Message = ProfileResponseMessageDTO.ChangePasswordSuccess, StatusCode = 200 };
    }

    // Lấy thông tin cá nhân của user dựa trên userId lấy từ JWT.
    public async Task<HTTPResponseData<ProfileResponse?>> GetProfileAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByIdWithRolesAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new HTTPResponseData<ProfileResponse?>
            {
                DataResponse = null,
                Message = ProfileResponseMessageDTO.ProfileNotFound,
                StatusCode = 404
            };
        }

        return new HTTPResponseData<ProfileResponse?>
        {
            DataResponse = Map(user),
            Message = ProfileResponseMessageDTO.GetProfileSuccess,
            StatusCode = 200
        };
    }

    // Cập nhật thông tin user đang đăng nhập: full name và phone.
    public async Task<HTTPResponseData<ProfileResponse?>> UpdateProfileAsync(
        int userId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByIdWithRolesAsync(userId, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return new HTTPResponseData<ProfileResponse?>
            {
                DataResponse = null,
                Message = ProfileResponseMessageDTO.ProfileNotFound,
                StatusCode = 404
            };
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            user.FullName = request.FullName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Phone))
        {
            user.Phone = request.Phone.Trim();
        }

        user.UpdatedAt = DateTime.UtcNow;
        unitOfWork.UserRepository.Update(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new HTTPResponseData<ProfileResponse?>
        {
            DataResponse = Map(user),
            Message = ProfileResponseMessageDTO.UpdateProfileSuccess,
            StatusCode = 200
        };
    }

    // Chuyển entity User sang DTO phẳng để trả về cho client.
    private static ProfileResponse Map(User user) => new(
        user.Id,
        user.FullName ?? user.Username,
        user.Email,
        user.Phone,
        user.UserRoles
            .Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray() is { Length: > 0 } roles ? roles : ["Patient"],
        user.IsActive,
        user.Doctor?.Id,
        user.Patient?.Id);
}

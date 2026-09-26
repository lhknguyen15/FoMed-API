using FoMed.Application.DTO.Auth;
using FoMed.Application.DTO;
using FoMed.Infrastructure.Authentication;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.UnitOfWork;

namespace FoMed.Application.Services.Auth;

public sealed class AuthService(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService)
{
    // Đăng nhập: kiểm tra email, trạng thái user và mật khẩu hash.
    public async Task<HTTPResponseData<AuthResponse?>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        // Lấy user theo email; repository đã include role để tạo token đúng role.
        var user = await unitOfWork.UserRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return new HTTPResponseData<AuthResponse?>
            {
                DataResponse = null,
                Message = AuthResponseMessageDTO.InvalidCredentials,
                StatusCode = 401
            };
        }

        return new HTTPResponseData<AuthResponse?>
        {
            DataResponse = CreateResponse(user),
            Message = AuthResponseMessageDTO.LoginSuccess,
            StatusCode = 200
        };
    }

    // Đăng ký: kiểm tra email trùng, tạo user mới và lưu vào database.
    public async Task<HTTPResponseData<AuthResponse?>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        // Không cho đăng ký email đã tồn tại.
        if (await unitOfWork.UserRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return new HTTPResponseData<AuthResponse?>
            {
                DataResponse = null,
                Message = AuthResponseMessageDTO.EmailAlreadyUsed,
                StatusCode = 409
            };
        }

        var patientRole = await unitOfWork.UserRepository.GetRoleByNameAsync("Patient", cancellationToken)
            ?? throw new InvalidOperationException("Patient role is missing. Initialize the database roles before registering users.");
        var patientCode = await unitOfWork.PatientRepository.GeneratePatientCodeAsync(cancellationToken);

        // Lưu cả user, role và hồ sơ bệnh nhân trong một lần SaveChanges (một transaction).
        var user = new User
        {
            Username = email,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        user.UserRoles.Add(new UserRole { User = user, Role = patientRole, RoleId = patientRole.Id });
        user.Patient = new FoMed.Infrastructure.Models.Patient
        {
            User = user,
            PatientCode = patientCode,
            FullName = user.FullName,
            IsActive = true,
            CreatedAt = user.CreatedAt
        };
        await unitOfWork.UserRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new HTTPResponseData<AuthResponse?>
        {
            DataResponse = CreateResponse(user),
            Message = AuthResponseMessageDTO.RegisterSuccess,
            StatusCode = 201
        };
    }

    // Tạo payload trả về cho client kèm access token.
    private AuthResponse CreateResponse(User user) =>
        new(
            user.Id,
            user.FullName ?? user.Username,
            user.Email,
            user.UserRoles.FirstOrDefault()?.Role.Name ?? "Patient",
            tokenService.CreateToken(user));
}

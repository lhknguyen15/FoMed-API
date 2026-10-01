using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Auth;
using FoMed.Infrastructure.Authentication;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.UnitOfWork;
using Microsoft.Extensions.Options;

namespace FoMed.Application.Services.Auth;

public sealed class AuthService(
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IOptions<JwtOptions> jwtOptions)
{
    public async Task<HTTPResponseData<AuthResponse?>> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await unitOfWork.UserRepository.GetByLoginAsync(
            request.Username.Trim().ToLowerInvariant(), cancellationToken);
        if (user is null || !passwordHasher.Verify(request.Password, user.PasswordHash))
            return Failure<AuthResponse?>(AuthResponseMessageDTO.InvalidCredentials, 401);
        if (!user.IsActive)
            return Failure<AuthResponse?>(AuthResponseMessageDTO.AccountInactive, 403);

        return new HTTPResponseData<AuthResponse?>
        {
            DataResponse = await CreateSessionResponseAsync(user, cancellationToken),
            Message = AuthResponseMessageDTO.LoginSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<AuthResponse?>> RefreshAsync(
        RefreshTokenRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await unitOfWork.UserRepository.GetRefreshTokenByHashAsync(
            HashRefreshToken(request.RefreshToken), cancellationToken);
        var now = DateTime.UtcNow;
        if (storedToken is null || storedToken.RevokedAt.HasValue ||
            storedToken.ExpiresAt <= now || !storedToken.User.IsActive)
            return Failure<AuthResponse?>(AuthResponseMessageDTO.InvalidRefreshToken, 401);

        storedToken.RevokedAt = now;
        return new HTTPResponseData<AuthResponse?>
        {
            DataResponse = await CreateSessionResponseAsync(storedToken.User, cancellationToken),
            Message = AuthResponseMessageDTO.LoginSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<string?>> ForgotPasswordAsync(
        ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            return Failure<string?>(errors[0].ErrorMessage ?? "Email không hợp lệ.", 400);

        var email = request.Email.Trim().ToLowerInvariant();
        _ = await unitOfWork.UserRepository.GetByEmailAsync(email, cancellationToken);
        return new HTTPResponseData<string?>
        {
            DataResponse = null,
            Message = AuthResponseMessageDTO.ForgotPasswordAccepted,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<AuthResponse?>> RegisterPatientAsync(
        RegisterPatientRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), errors, true))
            return Failure<AuthResponse?>(errors[0].ErrorMessage ?? "Dữ liệu đăng ký không hợp lệ.", 400);

        await using var writeScope = await unitOfWork.BeginWriteAsync(cancellationToken);
        var phone = FoMed.Application.Services.Patient.PatientService.NormalizePhone(request.Phone);
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant();
        var username = email ?? phone;
        if (await unitOfWork.UserRepository.GetByLoginAsync(username, cancellationToken) is not null ||
            (email is not null && await unitOfWork.UserRepository.GetByEmailAsync(email, cancellationToken) is not null))
            return Failure<AuthResponse?>(AuthResponseMessageDTO.AccountAlreadyUsed, 409);

        var patientRole = await unitOfWork.UserRepository.GetRoleByNameAsync("Patient", cancellationToken)
            ?? throw new InvalidOperationException("Patient role is missing. Initialize the database roles before registering users.");
        var dateOfBirth = request.DateOfBirth.HasValue
            ? DateOnly.FromDateTime(request.DateOfBirth.Value)
            : (DateOnly?)null;
        var candidates = await unitOfWork.PatientRepository.FindByPhoneAsync(phone, cancellationToken);
        var matchingCandidates = candidates.Where(patient =>
            patient.UserId is null &&
            request.DateOfBirth.HasValue &&
            string.Equals(patient.FullName.Trim(), request.FullName.Trim(), StringComparison.OrdinalIgnoreCase) &&
            patient.DateOfBirth == dateOfBirth).ToList();
        if (matchingCandidates.Count > 1)
            return Failure<AuthResponse?>(AuthResponseMessageDTO.PatientLinkAmbiguous, 409);

        var now = DateTime.UtcNow;
        var user = new User
        {
            Username = username,
            Email = email,
            Phone = phone,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAt = now
        };
        user.UserRoles.Add(new UserRole { User = user, Role = patientRole, RoleId = patientRole.Id });

        if (matchingCandidates.Count == 0)
        {
            user.Patient = new FoMed.Infrastructure.Models.Patient
            {
                User = user,
                PatientCode = await unitOfWork.PatientRepository.GeneratePatientCodeAsync(cancellationToken),
                FullName = user.FullName,
                DateOfBirth = dateOfBirth,
                Phone = phone,
                IsActive = true,
                CreatedAt = now
            };
        }
        else
        {
            var patient = matchingCandidates[0];
            patient.User = user;
            user.Patient = patient;
            patient.FullName = user.FullName;
            patient.DateOfBirth = dateOfBirth;
            patient.Phone = phone;
            patient.IsActive = true;
            unitOfWork.PatientRepository.Update(patient);
        }

        await unitOfWork.UserRepository.AddAsync(user, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        var session = await CreateSessionResponseAsync(user, cancellationToken);
        await writeScope.CommitAsync(cancellationToken);
        return new HTTPResponseData<AuthResponse?>
        {
            DataResponse = session,
            Message = AuthResponseMessageDTO.RegisterSuccess,
            StatusCode = 201
        };
    }

    private async Task<AuthResponse> CreateSessionResponseAsync(User user, CancellationToken cancellationToken)
    {
        var rawRefreshToken = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTime.UtcNow;
        user.RefreshTokens.Add(new RefreshToken
        {
            User = user,
            UserId = user.Id,
            TokenHash = HashRefreshToken(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(jwtOptions.Value.RefreshTokenDays)
        });
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var roles = user.UserRoles.Select(userRole => userRole.Role.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roles.Length == 0) roles = ["Patient"];
        return new AuthResponse(
            tokenService.CreateToken(user),
            rawRefreshToken,
            checked(jwtOptions.Value.ExpirationMinutes * 60),
            new AuthenticatedUserResponse(
                user.Id, user.FullName ?? user.Username, roles, user.Doctor?.Id, user.Patient?.Id));
    }

    private static HTTPResponseData<T> Failure<T>(string message, int statusCode) => new()
    {
        DataResponse = default!,
        Message = message,
        StatusCode = statusCode
    };

    private static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

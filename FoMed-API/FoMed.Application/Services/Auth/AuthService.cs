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
    public async Task<HTTPResponseData<AuthResponse?>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
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

    public async Task<HTTPResponseData<AuthResponse?>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await unitOfWork.UserRepository.GetByEmailAsync(email, cancellationToken) is not null)
        {
            return new HTTPResponseData<AuthResponse?>
            {
                DataResponse = null,
                Message = AuthResponseMessageDTO.EmailAlreadyUsed,
                StatusCode = 409
            };
        }

        var user = new User
        {
            Username = email,
            Email = email,
            FullName = request.FullName.Trim(),
            PasswordHash = passwordHasher.Hash(request.Password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
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

    private AuthResponse CreateResponse(User user) =>
        new(
            user.Id,
            user.FullName ?? user.Username,
            user.Email,
            user.UserRoles.FirstOrDefault()?.Role.Name ?? "Patient",
            tokenService.CreateToken(user));
}
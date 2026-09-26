using FoMed.Application.DTO.Auth;
using FoMed.Application.DTO;
using FoMed.Application.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    // Đăng ký tài khoản mới cho người dùng.
    [HttpPost("register")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        // Gọi service nghiệp vụ để validate, mã hóa mật khẩu, lưu user và tạo token.
        var response = await authService.RegisterAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    // Đăng nhập và trả về JWT token nếu thông tin hợp lệ.
    [HttpPost("login")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        // Service kiểm tra email và mật khẩu, sau đó tạo token cho client.
        var response = await authService.LoginAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }
}
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
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Register(
        RegisterPatientRequest request,
        CancellationToken cancellationToken)
    {
        // Gọi service nghiệp vụ để validate, mã hóa mật khẩu, lưu user và tạo token.
        var response = await authService.RegisterPatientAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    // Đăng nhập và trả về JWT token nếu thông tin hợp lệ.
    [HttpPost("login")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        // Service kiểm tra email và mật khẩu, sau đó tạo token cho client.
        var response = await authService.LoginAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    // Đổi refresh token một lần để tiếp tục phiên đăng nhập.
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Refresh(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RefreshAsync(request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Gửi yêu cầu khôi phục mật khẩu theo email; response không tiết lộ email có tồn tại.
    [HttpPost("forgot-password")]
    [ProducesResponseType(typeof(HTTPResponseData<string?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<string?>), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<HTTPResponseData<string?>>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.ForgotPasswordAsync(request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }
}

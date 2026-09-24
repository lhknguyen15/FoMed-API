using FoMed.Application.DTO.Auth;
using FoMed.Application.DTO;
using FoMed.Application.Services.Auth;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(AuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Register(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.RegisterAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<AuthResponse?>), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HTTPResponseData<AuthResponse?>>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);

        return StatusCode(response.StatusCode, response);
    }
}
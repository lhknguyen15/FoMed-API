using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Profile;
using FoMed.Application.Services.Profile;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/profile")]
[Authorize]
public sealed class ProfileController(ProfileService profileService) : ControllerBase
{
    [HttpPut("change-password")]
    [ProducesResponseType(typeof(HTTPResponseData<string?>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<string?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<string?>>> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(new HTTPResponseData<string?>
            {
                DataResponse = null, Message = "Token không hợp lệ.", StatusCode = 401
            });

        var response = await profileService.ChangePasswordAsync(userId.Value, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Lấy thông tin profile của người dùng đang đăng nhập.
    [HttpGet]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<ProfileResponse?>>> GetProfile(CancellationToken cancellationToken)
    {
        // Lấy userId từ token JWT để xác định người dùng đang thao tác.
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<ProfileResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await profileService.GetProfileAsync(userId.Value, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Cập nhật thông tin cá nhân của chính user đang đăng nhập.
    [HttpPut]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<ProfileResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<ProfileResponse?>>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        // Không cho phép cập nhật profile của người khác; chỉ dùng token hiện tại.
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<ProfileResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await profileService.UpdateProfileAsync(userId.Value, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Trích userId từ claim trong JWT.
    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(claim, out var userId) ? userId : null;
    }
}

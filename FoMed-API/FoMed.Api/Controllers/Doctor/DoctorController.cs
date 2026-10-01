using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/doctor")]
[Authorize]
public sealed class DoctorController(DoctorService doctorService) : ControllerBase
{
    // Lấy hồ sơ bác sĩ của tài khoản đang đăng nhập.
    [HttpGet("me")]
    [Authorize(Roles = "Doctor")]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<DoctorResponse?>>> GetMyProfile(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<DoctorResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await doctorService.GetMyProfileAsync(userId.Value, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Cập nhật hồ sơ bác sĩ của chính tài khoản đang đăng nhập.
    [HttpPut("me")]
    [Authorize(Roles = "Doctor")]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<DoctorResponse?>>> UpdateMyProfile(
        [FromBody] UpdateDoctorProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<DoctorResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await doctorService.UpdateMyProfileAsync(
            userId.Value,
            request,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    // Đọc userId từ claim sub do JwtTokenService tạo ra.
    private int? GetCurrentUserId()
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(claim, out var userId) ? userId : null;
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Application.Services.Patient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/patient")]
[Authorize(Roles = "Patient")]
public sealed class PatientController(PatientService patientService) : ControllerBase
{
    // Lấy hồ sơ bệnh nhân của tài khoản đang đăng nhập.
    [HttpGet("me")]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<PatientResponse?>>> GetMyProfile(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<PatientResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await patientService.GetMyProfileAsync(userId.Value, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Cập nhật hồ sơ bệnh nhân của chính tài khoản đang đăng nhập.
    [HttpPut("me")]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<PatientResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<PatientResponse?>>> UpdateMyProfile(
        [FromBody] UpdatePatientProfileRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized(new HTTPResponseData<PatientResponse?>
            {
                DataResponse = null,
                Message = "Token không hợp lệ.",
                StatusCode = 401
            });
        }

        var response = await patientService.UpdateMyProfileAsync(
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

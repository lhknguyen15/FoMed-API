using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/doctor/schedules")]
[Authorize(Roles = "Doctor")]
public sealed class DoctorScheduleController(DoctorScheduleService scheduleService) : ControllerBase
{
    // Lấy lịch làm việc của bác sĩ đang đăng nhập.
    [HttpGet]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>>> GetMySchedules(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await scheduleService.GetMySchedulesAsync(userId.Value, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Tạo một khung giờ làm việc mới.
    [HttpPost]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<DoctorScheduleResponse?>>> Create(
        [FromBody] SaveDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await scheduleService.CreateAsync(userId.Value, request, cancellationToken);
        return StatusCode(response.StatusCode, response);
    }

    // Cập nhật một khung giờ thuộc về bác sĩ đang đăng nhập.
    [HttpPut("{scheduleId:int}")]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<HTTPResponseData<DoctorScheduleResponse?>>> Update(
        int scheduleId,
        [FromBody] SaveDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await scheduleService.UpdateAsync(
            userId.Value,
            scheduleId,
            request,
            cancellationToken);

        return StatusCode(response.StatusCode, response);
    }

    // Xóa mềm một khung giờ làm việc.
    [HttpDelete("{scheduleId:int}")]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(HTTPResponseData<DoctorScheduleResponse?>), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HTTPResponseData<DoctorScheduleResponse?>>> Delete(
        int scheduleId,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var response = await scheduleService.DeleteAsync(
            userId.Value,
            scheduleId,
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

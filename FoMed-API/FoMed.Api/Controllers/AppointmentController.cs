using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Appointment;
using FoMed.Application.Services.Appointment;
using FoMed.Infrastructure.Models.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController]
[Route("api/appointments")]
[Authorize]
public sealed class AppointmentController(AppointmentService appointmentService) : ControllerBase
{
    private readonly AppointmentService _service = appointmentService;

    // GET api/appointments/available-slots?doctorId=1&date=2026-09-30
    [HttpGet("available-slots")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>), 404)]
    public async Task<ActionResult> GetAvailableSlots(
        [FromQuery] int doctorId, [FromQuery] DateOnly date, CancellationToken ct)
    {
        var res = await _service.GetAvailableSlotsAsync(doctorId, date, ct);
        return StatusCode(res.StatusCode, res);
    }

    // POST api/appointments/book
    [HttpPost("book")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 201)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 404)]
    public async Task<ActionResult> BookAppointment([FromBody] BookAppointmentRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.BookAppointmentAsync(uid.Value, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/my-appointments
    [HttpGet("my-appointments")]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 200)]
    public async Task<ActionResult> GetMyAppointments(
        [FromQuery] DateOnly? date, [FromQuery] AppointmentStatus? status, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetPatientAppointmentsAsync(uid.Value, date, status, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/doctor-appointments
    [HttpGet("doctor-appointments")]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 200)]
    public async Task<ActionResult> GetDoctorAppointments(
        [FromQuery] DateOnly? date, [FromQuery] AppointmentStatus? status, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetDoctorAppointmentsAsync(uid.Value, date, status, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/{id}
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 404)]
    public async Task<ActionResult> GetById([FromRoute] int id, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetAppointmentByIdAsync(uid.Value, id, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/confirm
    [HttpPut("{id:int}/confirm")]
    [Authorize(Roles = "Doctor,Receptionist")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    public async Task<ActionResult> Confirm([FromRoute] int id, [FromBody] ChangeAppointmentStatusRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.ConfirmAppointmentAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/complete
    [HttpPut("{id:int}/complete")]
    [Authorize(Roles = "Doctor")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    public async Task<ActionResult> Complete([FromRoute] int id, [FromBody] ChangeAppointmentStatusRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.CompleteAppointmentAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/cancel
    [HttpPut("{id:int}/cancel")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    public async Task<ActionResult> Cancel([FromRoute] int id, [FromBody] ChangeAppointmentStatusRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.CancelAppointmentAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/{id}/history
    [HttpGet("{id:int}/history")]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>), 404)]
    public async Task<ActionResult> GetHistory([FromRoute] int id, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetStatusHistoryAsync(uid.Value, id, ct);
        return StatusCode(res.StatusCode, res);
    }

    private int? GetUserId()
    {
        var claim = User.FindFirstValue(JwtRegisteredClaimNames.Sub)
                 ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : null;
    }

    private static HTTPResponseData<object?> Unauth() =>
        new() { DataResponse = null, Message = "Token khong hop le.", StatusCode = 401 };
}

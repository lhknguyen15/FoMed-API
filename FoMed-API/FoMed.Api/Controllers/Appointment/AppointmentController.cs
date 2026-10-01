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
        [FromQuery] int doctorId, [FromQuery] DateOnly date, [FromQuery] int? serviceId, CancellationToken ct)
    {
        var res = await _service.GetAvailableSlotsAsync(doctorId, date, ct, serviceId);
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

    // POST api/appointments/staff-book
    [HttpPost("staff-book")]
    [Authorize(Roles = "Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 201)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 409)]
    public async Task<ActionResult> StaffBook([FromBody] StaffBookAppointmentRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.BookAppointmentForPatientAsync(uid.Value, request, ct);
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

    // GET api/appointments/staff-appointments
    [HttpGet("staff-appointments")]
    [Authorize(Roles = "Doctor,Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 403)]
    public async Task<ActionResult> GetStaffAppointments(
        [FromQuery] DateOnly? date, [FromQuery] AppointmentStatus? status, [FromQuery] int? doctorId, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetStaffAppointmentsAsync(uid.Value, date, status, doctorId, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/waiting-queue?date=2026-09-30
    [HttpGet("waiting-queue")]
    [Authorize(Roles = "Doctor,Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<IReadOnlyList<AppointmentResponse>>), 403)]
    public async Task<ActionResult> GetWaitingQueue([FromQuery] DateOnly? date, [FromQuery] int? doctorId, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetWaitingQueueAsync(uid.Value, date, doctorId, ct);
        return StatusCode(res.StatusCode, res);
    }

    // GET api/appointments/doctor-queue?date=2026-09-30
    [HttpGet("doctor-queue")]
    [Authorize(Roles = "Doctor")]
    public async Task<ActionResult> GetDoctorQueue([FromQuery] DateOnly? date, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.GetDoctorQueueAsync(uid.Value, date, ct);
        return StatusCode(res.StatusCode, res);
    }

    // POST api/appointments/call-next?date=2026-09-30&doctorId=1
    [HttpPost("call-next")]
    [Authorize(Roles = "Doctor,Receptionist,Admin")]
    public async Task<ActionResult> CallNext([FromQuery] DateOnly? date, [FromQuery] int? doctorId,
        [FromBody] QueueActionRequest? request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.CallNextAsync(uid.Value, date, doctorId, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/move-to-end
    [HttpPut("{id:int}/move-to-end")]
    [Authorize(Roles = "Doctor,Receptionist,Admin")]
    public async Task<ActionResult> MoveToEnd([FromRoute] int id, [FromBody] QueueActionRequest? request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.MoveToEndAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/check-in
    [HttpPut("{id:int}/check-in")]
    [Authorize(Roles = "Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 404)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 409)]
    public async Task<ActionResult> CheckIn([FromRoute] int id, [FromBody] ChangeAppointmentStatusRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.CheckInAppointmentAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/no-show
    [HttpPut("{id:int}/no-show")]
    [Authorize(Roles = "Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 404)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 409)]
    public async Task<ActionResult> NoShow([FromRoute] int id, [FromBody] ChangeAppointmentStatusRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.MarkNoShowAsync(uid.Value, id, request, ct);
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

    // PUT api/appointments/{id}/reschedule
    [HttpPut("{id:int}/reschedule")]
    [Authorize(Roles = "Patient")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 403)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 404)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 409)]
    public async Task<ActionResult> Reschedule(
        [FromRoute] int id, [FromBody] RescheduleAppointmentRequest request, CancellationToken ct)
    {
        var uid = GetUserId();
        if (uid is null) return Unauthorized(Unauth());
        var res = await _service.RescheduleAppointmentAsync(uid.Value, id, request, ct);
        return StatusCode(res.StatusCode, res);
    }

    // PUT api/appointments/{id}/confirm
    [HttpPut("{id:int}/confirm")]
    [Authorize(Roles = "Doctor,Receptionist,Admin")]
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
    [Authorize(Roles = "Patient,Doctor,Receptionist,Admin")]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 200)]
    [ProducesResponseType(typeof(HTTPResponseData<AppointmentResponse?>), 400)]
    public async Task<ActionResult> Cancel([FromRoute] int id, [FromBody] CancelAppointmentRequest request, CancellationToken ct)
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

using System.Security.Claims;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers.Reception;

[ApiController]
[Route("api/reception/schedules")]
[Authorize(Roles = "Receptionist,Admin")]
public sealed class ReceptionDoctorScheduleController(AdminDoctorScheduleService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? doctorId, CancellationToken ct) => Ok(await service.ListAsync(UserId, doctorId, ct, reception: true));
    [HttpGet("doctors")]
    public async Task<IActionResult> Doctors(CancellationToken ct) => Ok(await service.ReceptionDoctorsAsync(UserId, ct));
    // Read only. Leave changes remain restricted to existing Admin/own-doctor endpoints.
    [HttpGet("time-off")]
    public async Task<IActionResult> TimeOff([FromQuery] int? doctorId, CancellationToken ct) => Ok(await service.ReceptionTimeOffAsync(UserId, doctorId, ct));
    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch([FromBody] SaveAdminDoctorScheduleBatchRequest request, CancellationToken ct) => StatusCode(201, await service.CreateBatchAsync(UserId, request, ct, reception: true));
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveAdminDoctorScheduleRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(UserId, id, request, ct, reception: true));
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Deactivate(int id, [FromQuery] string? expectedVersion, CancellationToken ct)
    {
        await service.DeleteAsync(UserId, id, ct, reception: true, expectedVersion: expectedVersion);
        return NoContent();
    }
}

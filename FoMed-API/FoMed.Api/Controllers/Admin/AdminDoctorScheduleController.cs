using System.Security.Claims;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers.Admin;

[ApiController]
[Route("api/admin/schedules")]
[Authorize(Roles = "Admin")]
public sealed class AdminDoctorScheduleController(AdminDoctorScheduleService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? doctorId, CancellationToken ct) => Ok(await service.ListAsync(UserId, doctorId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveAdminDoctorScheduleRequest request, CancellationToken ct) => StatusCode(201, await service.CreateAsync(UserId, request, ct));

    [HttpPost("batch")]
    public async Task<IActionResult> CreateBatch([FromBody] SaveAdminDoctorScheduleBatchRequest request, CancellationToken ct) => StatusCode(201, await service.CreateBatchAsync(UserId, request, ct));

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveAdminDoctorScheduleRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(UserId, id, request, ct));

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await service.DeleteAsync(UserId, id, ct);
        return NoContent();
    }
}

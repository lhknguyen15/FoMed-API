using System.Security.Claims;
using FoMed.Application.DTO.Doctor;
using FoMed.Application.Services.Doctor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/doctor/time-off"), Authorize(Roles = "Doctor")]
public sealed class DoctorTimeOffController(DoctorTimeOffService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(UserId, false, null, ct));
    [HttpPost] public async Task<IActionResult> Create(SaveDoctorTimeOffRequest request, CancellationToken ct) => StatusCode(201, await service.CreateAsync(UserId, request, false, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SaveDoctorTimeOffRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(UserId, id, request, false, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.DeleteAsync(UserId, id, false, ct); return NoContent(); }
}

[ApiController, Route("api/admin/time-off"), Authorize(Roles = "Admin")]
public sealed class AdminTimeOffController(DoctorTimeOffService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    [HttpGet] public async Task<IActionResult> List([FromQuery] int? doctorId, CancellationToken ct) => Ok(await service.ListAsync(UserId, true, doctorId, ct));
    [HttpPost] public async Task<IActionResult> Create(SaveDoctorTimeOffRequest request, CancellationToken ct) => StatusCode(201, await service.CreateAsync(UserId, request, true, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SaveDoctorTimeOffRequest request, CancellationToken ct) => Ok(await service.UpdateAsync(UserId, id, request, true, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.DeleteAsync(UserId, id, true, ct); return NoContent(); }
}

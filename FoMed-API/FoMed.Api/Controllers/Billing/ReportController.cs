using System.Security.Claims;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoMed.Api.Controllers;
[ApiController, Route("api/reports"), Authorize(Roles = "Receptionist,Admin")]
public sealed class ReportController(ReportService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet("summary")] public async Task<IActionResult> Summary([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? doctorId, CancellationToken ct) => Ok(await service.SummaryAsync(UserId, new ReportQuery(from, to, doctorId), ct));
    [HttpGet("export")] public async Task<IActionResult> Export([FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int? doctorId, CancellationToken ct) { var file = await service.ExportCsvAsync(UserId, new ReportQuery(from, to, doctorId), ct); return File(file.Content, "text/csv; charset=utf-8", file.FileName); }
}

using System.Security.Claims;
using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Auth;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoMed.Api.Controllers;
[ApiController, Route("api/audit-logs"), Authorize(Roles = "Admin")]
public sealed class AuditLogController(AuditLogService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List([FromQuery] string? entity, [FromQuery] string? action, [FromQuery] int? userId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) { var result = await service.ListAsync(UserId, new AuditLogQuery(entity, action, userId, from, to, page, pageSize), ct); return Ok(new { result.Items, result.Total, Page = page, PageSize = pageSize }); }
}

using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Auth;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoMed.Api.Controllers;
[ApiController, Route("api/admin/users"), Authorize(Roles = "Admin")]
public sealed class AdminUserController(AdminUserService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? role, [FromQuery] bool? isActive, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default) { var result = await service.ListAsync(UserId, search, role, isActive, page, pageSize, ct); return Ok(new HTTPResponseData<object> { DataResponse = new { result.Items, result.Total, Page = page, PageSize = pageSize }, Message = "Thành công.", StatusCode = 200 }); }
    [HttpPut("{id:int}/status")] public async Task<IActionResult> Status(int id, UpdateUserStatusRequest request, CancellationToken ct) => Result(await service.UpdateStatusAsync(UserId, id, request, ct));
    [HttpPut("{id:int}/roles")] public async Task<IActionResult> Roles(int id, UpdateUserRolesRequest request, CancellationToken ct) => Result(await service.UpdateRolesAsync(UserId, id, request, ct));
    [HttpPost("{id:int}/reset-password")] public async Task<IActionResult> ResetPassword(int id, ResetUserPasswordRequest request, CancellationToken ct) => Result(await service.ResetPasswordAsync(UserId, id, request, ct));
    private ObjectResult Result<T>(HTTPResponseData<T> response) => StatusCode(response.StatusCode, response);
}
[ApiController, Route("api/admin/roles"), Authorize(Roles = "Admin")]
public sealed class AdminRoleController(AdminUserService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(new HTTPResponseData<IReadOnlyList<AdminRoleResponse>> { DataResponse = await service.ListRolesAsync(UserId, ct), Message = "Thành công.", StatusCode = 200 });
}

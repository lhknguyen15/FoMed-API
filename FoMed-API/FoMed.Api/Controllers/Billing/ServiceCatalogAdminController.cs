using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoMed.Api.Controllers;
[ApiController, Route("api/admin/services"), Authorize(Roles = "Admin")]
public sealed class ServiceCatalogAdminController(ServiceCatalogAdminService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Result(await service.ListAsync(UserId, ct));
    [HttpPost] public async Task<IActionResult> Create(CreateServiceRequest request, CancellationToken ct) => Result(await service.CreateAsync(UserId, request, ct), 201);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, UpdateServiceRequest request, CancellationToken ct) => Result(await service.UpdateAsync(UserId, id, request, ct));
    private ObjectResult Result<T>(HTTPResponseData<T> response, int? status = null) => StatusCode(status ?? response.StatusCode, response);
}

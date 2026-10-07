using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Clinical;
using FoMed.Application.Services.Pharmacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/admin/medicines"), Authorize(Roles = "Admin")]
public sealed class MedicineCatalogAdminController(MedicineCatalogAdminService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ClinicException(401, "Phiên đăng nhập không hợp lệ.");

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] MedicineCatalogQuery query, CancellationToken ct) =>
        Result(await service.ListAsync(UserId, query, ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Result(await service.GetAsync(UserId, id, ct));
    [HttpPost]
    public async Task<IActionResult> Create(SaveMedicineRequest request, CancellationToken ct) =>
        Result(await service.CreateAsync(UserId, request, ct), 201);
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, UpdateMedicineRequest request, CancellationToken ct) =>
        Result(await service.UpdateAsync(UserId, id, request, ct));
    [HttpPut("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, MedicineStatusRequest request, CancellationToken ct) =>
        Result(await service.SetStatusAsync(UserId, id, request, ct));

    private ObjectResult Result<T>(T value, int status = 200) => StatusCode(status,
        new HTTPResponseData<T> { DataResponse = value, StatusCode = status, Message = "Thành công." });
}

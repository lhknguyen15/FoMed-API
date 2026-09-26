using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Clinical;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/clinical"), Authorize]
public sealed class ClinicalController(ClinicalService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ClinicException(401, "Token không hợp lệ.");
    private ObjectResult Result<T>(T data, int status = 200) => StatusCode(status, new HTTPResponseData<T> { DataResponse = data, Message = "Thành công.", StatusCode = status });

    [HttpGet("records")]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1) => Result(await service.ListAsync(UserId, page, ct));
    [HttpPost("appointments/{appointmentId:int}/record"), Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Create(int appointmentId, SaveMedicalRecordRequest request, CancellationToken ct) => Result(await service.CreateRecordAsync(UserId, appointmentId, request, ct), 201);
    [HttpGet("records/{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Result(await service.GetRecordAsync(UserId, id, ct));
    [HttpPut("records/{id:int}"), Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Update(int id, SaveMedicalRecordRequest request, CancellationToken ct) => Result(await service.UpdateRecordAsync(UserId, id, request, ct));
    [HttpPost("records/{id:int}/prescription"), Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Prescribe(int id, CreatePrescriptionRequest request, CancellationToken ct) => Result(await service.CreatePrescriptionAsync(UserId, id, request, ct), 201);
    [HttpGet("records/{id:int}/prescription")]
    public async Task<IActionResult> Prescription(int id, CancellationToken ct) => Result(await service.GetPrescriptionAsync(UserId, id, ct));
    [HttpPost("records/{id:int}/services"), Authorize(Roles = "Doctor")]
    public async Task<IActionResult> Order(int id, OrderServiceRequest request, CancellationToken ct) => Result(await service.OrderServiceAsync(UserId, id, request, ct), 201);
    [HttpGet("records/{id:int}/services")]
    public async Task<IActionResult> Orders(int id, CancellationToken ct) => Result(await service.GetOrdersAsync(UserId, id, ct));
    [HttpGet("lab-orders"), Authorize(Roles = "Technician")]
    public async Task<IActionResult> Pending(CancellationToken ct, [FromQuery] int page = 1) => Result(await service.PendingTestsAsync(UserId, page, ct));
    [HttpPost("lab-orders/{id:int}/result"), Authorize(Roles = "Technician")]
    public async Task<IActionResult> Result(int id, SaveLabResultRequest request, CancellationToken ct) => Result(await service.SaveResultAsync(UserId, id, request, ct), 201);
    [HttpGet("medicines")]
    public async Task<IActionResult> Medicines(CancellationToken ct, [FromQuery] int page = 1) => Result(await service.CatalogAsync(true, page, ct));
    [HttpGet("services")]
    public async Task<IActionResult> Services(CancellationToken ct, [FromQuery] int page = 1) => Result(await service.CatalogAsync(false, page, ct));
}

using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FoMed.Api.Controllers;

[ApiController, Route("api/invoices"), Authorize]
public sealed class InvoiceController(BillingService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    private ObjectResult Result<T>(T data, int status = 200) => StatusCode(status, new HTTPResponseData<T> { DataResponse = data, Message = "Thành công.", StatusCode = status });
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct, [FromQuery] int page = 1) => Result(await service.ListAsync(UserId, page, ct));
    [HttpGet("search"), Authorize(Roles = "Receptionist,Admin")]
    public async Task<IActionResult> Search([FromQuery] InvoiceSearchRequest request, CancellationToken ct) =>
        Result(await service.SearchAsync(UserId, request, ct));
    [HttpGet("eligible"), Authorize(Roles = "Receptionist,Admin")]
    public async Task<IActionResult> Eligible(CancellationToken ct, [FromQuery] int page = 1) =>
        Result(await service.ListEligibleAsync(UserId, page, ct));
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct) => Result(await service.GetAsync(UserId, id, ct));
    [HttpPost, Authorize(Roles = "Receptionist,Admin")]
    public async Task<IActionResult> Create(CreateInvoiceRequest request, CancellationToken ct) => Result(await service.CreateAsync(UserId, request, ct), 201);
    [HttpPost("{id:int}/payments"), Authorize(Roles = "Receptionist,Admin")]
    public async Task<IActionResult> Pay(int id, RecordPaymentRequest request, CancellationToken ct) => Result(await service.PayAsync(UserId, id, request, ct));
    [HttpPost("{id:int}/cancel"), Authorize(Roles = "Receptionist,Admin")]
    public async Task<IActionResult> Cancel(int id, CancelInvoiceRequest request, CancellationToken ct) => Result(await service.CancelAsync(UserId, id, request, ct));
}

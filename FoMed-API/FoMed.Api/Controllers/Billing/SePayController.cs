using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Authorize]
public sealed class SePayController(SePayService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new ClinicException(401, "Token không hợp lệ.");
    private IActionResult Result<T>(T data) => Ok(new HTTPResponseData<T> { DataResponse = data, Message = "Thành công.", StatusCode = 200 });

    [HttpPost("api/invoices/{id:int}/sepay/payment-requests"), Authorize(Roles = "Receptionist,Admin,Patient")]
    public async Task<IActionResult> Create(int id, CancellationToken ct) => Result(await service.CreateAsync(UserId, id, ct));

    [HttpGet("api/invoices/{id:int}/sepay/payment-requests/{requestId:guid}"), Authorize(Roles = "Receptionist,Admin,Patient")]
    public async Task<IActionResult> Get(int id, Guid requestId, CancellationToken ct) => Result(await service.GetAsync(UserId, id, requestId, ct));

    [HttpGet("api/sepay/transactions"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Transactions(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] string? status = null)
        => Result(await service.TransactionsAsync(UserId, page, status, ct));
}

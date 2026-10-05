using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Pharmacy;
using FoMed.Application.Services.Pharmacy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/pharmacy"), Authorize]
public sealed class PharmacyController(PharmacyService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : throw new FoMed.Application.Services.Clinical.ClinicException(401, "Token không hợp lệ.");
    private ObjectResult Result<T>(T data, int status = 200) => StatusCode(status,
        new HTTPResponseData<T> { DataResponse = data, Message = "Thành công.", StatusCode = status });

    [HttpGet("inventory"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Inventory(CancellationToken ct, [FromQuery] int page = 1, [FromQuery] int? medicineId = null, [FromQuery] DateOnly? expiringBefore = null) =>
        Result(await service.ListInventoryAsync(UserId, page, medicineId, expiringBefore, ct));

    [HttpPost("inventory/receipts"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Receive(ReceiveStockRequest request, CancellationToken ct) =>
        Result(await service.ReceiveStockAsync(UserId, request, ct), 201);

    [HttpPost("receipts"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> ReceiveReceipt(ReceiveStockReceiptRequest request, CancellationToken ct) =>
        Result(await service.ReceiveReceiptAsync(UserId, request, ct), 201);

    [HttpPost("inventory/adjustments"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Adjust(AdjustStockRequest request, CancellationToken ct) =>
        Result(await service.AdjustStockAsync(UserId, request, ct));

    [HttpGet("inventory/{batchId:int}/transactions"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Transactions(int batchId, CancellationToken ct, [FromQuery] int page = 1) =>
        Result(await service.ListTransactionsAsync(UserId, batchId, page, ct));

    [HttpGet("prescriptions/{id:int}"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Prescription(int id, CancellationToken ct) =>
        Result(await service.GetPrescriptionAsync(UserId, id, ct));

    [HttpGet("prescriptions"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Prescriptions(CancellationToken ct, [FromQuery] string? keyword = null,
        [FromQuery] string? status = "pending", [FromQuery] int page = 1) =>
        Result(await service.ListPrescriptionsAsync(UserId, keyword, status, page, ct));

    [HttpPost("prescriptions/{id:int}/dispense"), Authorize(Roles = "Pharmacist,Admin")]
    public async Task<IActionResult> Dispense(int id, CancellationToken ct) =>
        Result(await service.DispenseAsync(UserId, id, ct));
}

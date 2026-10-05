using System.Security.Claims;
using FoMed.Application.DTO;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/clinical"), Authorize]
public sealed class ClinicalAttachmentsController(ClinicalAttachmentService service) : ControllerBase
{
    private int UserId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ClinicException(401, "Token không hợp lệ.");
    private ObjectResult Result<T>(T data, int status = 200) => StatusCode(status, new HTTPResponseData<T> { DataResponse = data, Message = "Thành công.", StatusCode = status });
    [HttpGet("records/{recordId:int}/attachments")]
    public async Task<IActionResult> List(int recordId, CancellationToken ct, [FromQuery] int? orderId = null, [FromQuery] int page = 1) => Result(await service.ListAsync(UserId, recordId, orderId, page, ct));
    [HttpPost("records/{recordId:int}/attachments"), Authorize(Roles = "Doctor,Technician")]
    [RequestSizeLimit(ClinicalAttachmentService.MaxBytes + 65536)]
    [RequestFormLimits(MultipartBodyLengthLimit = ClinicalAttachmentService.MaxBytes + 65536)]
    public async Task<IActionResult> Upload(int recordId, [FromForm] IFormFile file, CancellationToken ct, [FromQuery] int? orderId = null)
    {
        if (file.Length is <= 0 or > ClinicalAttachmentService.MaxBytes) throw new ClinicException(400, "File phải có dữ liệu và không vượt quá 10 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return Result(await service.UploadAsync(UserId, recordId, orderId, file.FileName, buffer.ToArray(), ct), 201);
    }
    [HttpGet("attachments/{id:int}/download")]
    public async Task<IActionResult> Download(int id, CancellationToken ct)
    {
        var file = await service.DownloadAsync(UserId, id, ct);
        Response.Headers.CacheControl = "no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Bytes, file.ContentType, file.FileName);
    }
}

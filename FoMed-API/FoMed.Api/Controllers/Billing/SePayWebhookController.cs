using System.Security.Cryptography;
using System.Text.Json;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FoMed.Api.Controllers;

[ApiController, Route("api/webhooks/sepay")]
public sealed class SePayWebhookController(SePayWebhookAuthenticator authenticator, SePayService service) : ControllerBase
{
    private const int MaxBodyBytes = 16384;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // SePay cannot supply a FoMed JWT; this action authenticates the raw request with HMAC instead.
    [HttpPost, AllowAnonymous, RequestSizeLimit(MaxBodyBytes), Consumes("application/json")]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        var timestamp = Request.Headers["X-SePay-Timestamp"];
        var signature = Request.Headers["X-SePay-Signature"];
        if (timestamp.Count != 1 || signature.Count != 1)
            throw new ClinicException(401, "Thiếu chữ ký webhook SePay.");
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int read;
        while ((read = await Request.Body.ReadAsync(chunk.AsMemory(), ct)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes) throw new ClinicException(413, "Webhook SePay vượt giới hạn kích thước.");
            buffer.Write(chunk, 0, read);
        }
        var bytes = buffer.ToArray();
        authenticator.Verify(bytes, timestamp.ToString(), signature.ToString());
        SePayWebhookPayload payload;
        try
        {
            // Reject duplicate JSON field names (including alternate casing), avoiding ambiguous financial data.
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in document.RootElement.EnumerateObject())
                if (!names.Add(field.Name)) throw new JsonException();
            payload = JsonSerializer.Deserialize<SePayWebhookPayload>(bytes, JsonOptions) ?? throw new JsonException();
        }
        catch (JsonException) { throw new ClinicException(400, "JSON webhook SePay không hợp lệ."); }
        await service.ReceiveAsync(payload, Convert.ToHexString(SHA256.HashData(bytes)), ct);
        return Ok(new { success = true });
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace FoMed.Application.Services.Billing;

public sealed class SePayWebhookAuthenticator(IOptions<SePayOptions> options, TimeProvider clock)
{
    public void Verify(byte[] rawBody, string timestamp, string signature)
    {
        var config = options.Value;
        if (!config.Enabled) throw new ClinicException(503, "Thanh toán SePay chưa được bật.");
        if (!config.IsValid()) throw new ClinicException(503, "Cấu hình SePay chưa hợp lệ.");
        if (timestamp.Length > 20 || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var epoch)
            || Math.Abs((decimal)clock.GetUtcNow().ToUnixTimeSeconds() - epoch) > config.SignatureToleranceSeconds
            || signature.Length != 71 || !signature.StartsWith("sha256=", StringComparison.Ordinal))
            throw new ClinicException(401, "Webhook SePay không hợp lệ hoặc đã hết hạn.");
        byte[] received;
        try { received = Convert.FromHexString(signature[7..]); }
        catch (FormatException) { throw new ClinicException(401, "Chữ ký SePay không hợp lệ."); }
        var prefix = Encoding.UTF8.GetBytes(timestamp + ".");
        var signed = new byte[prefix.Length + rawBody.Length];
        prefix.CopyTo(signed, 0);
        rawBody.CopyTo(signed, prefix.Length);
        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.WebhookSecret), signed);
        if (!CryptographicOperations.FixedTimeEquals(expected, received))
            throw new ClinicException(401, "Chữ ký SePay không hợp lệ.");
    }
}

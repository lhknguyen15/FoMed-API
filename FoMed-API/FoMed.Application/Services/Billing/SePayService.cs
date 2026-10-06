using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Payments;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FoMed.Application.Services.Billing;

public sealed class SePayService(ClinicRepository repository, ClinicAccess access, IOptions<SePayOptions> options, TimeProvider clock)
{
    private static readonly Regex CodePattern = new("(?<![A-Za-z0-9])FM[A-F0-9]{24}(?![A-Za-z0-9])", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private SePayOptions Config
    {
        get
        {
            var value = options.Value;
            if (!value.Enabled || !value.IsValid()) throw new ClinicException(503, "Thanh toán SePay chưa được cấu hình hoặc chưa được bật.");
            return value;
        }
    }

    public async Task<SePayPaymentRequestResponse> CreateAsync(int userId, int invoiceId, CancellationToken ct)
    {
        var config = Config;
        await using var write = await repository.BeginWriteAsync(ct);
        var invoice = await InvoiceAsync(userId, invoiceId, ct);
        var remaining = Remaining(invoice);
        if (invoice.Status != 0 || remaining <= 0) throw new ClinicException(409, "Hóa đơn đã thanh toán hoặc đã hủy.");
        if (decimal.Truncate(remaining) != remaining) throw new ClinicException(409, "Thanh toán QR yêu cầu số tiền là số đồng nguyên. Cần đối soát hóa đơn.");
        if (await repository.Query<SePayPaymentRequest>().AnyAsync(r => r.InvoiceId == invoiceId && r.Environment == config.Environment && r.Status == "ReviewRequired", ct))
            throw new ClinicException(409, "Hóa đơn có chuyển khoản cần đối soát. Không tạo thêm yêu cầu thanh toán.");
        var now = clock.GetUtcNow().UtcDateTime;
        var pending = await repository.Query<SePayPaymentRequest>().SingleOrDefaultAsync(r => r.InvoiceId == invoiceId && r.Environment == config.Environment && r.Status == "Pending", ct);
        if (pending != null && pending.ExpiresAt > now && pending.Amount == remaining
            && pending.BankCode == config.BankCode && pending.Gateway == config.Gateway
            && pending.AccountNumber == config.AccountNumber && pending.AccountName == config.AccountName)
            return Map(pending, invoice, now);
        if (pending != null)
        {
            pending.Status = pending.ExpiresAt <= now ? "Expired" : "Superseded";
            // Release the filtered unique index before inserting a replacement in the same transaction.
            await repository.SaveAsync(ct);
        }
        var request = new SePayPaymentRequest
        {
            Id = Guid.NewGuid(), InvoiceId = invoiceId, Invoice = invoice, Environment = config.Environment,
            Code = "FM" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)), Amount = remaining,
            BankCode = config.BankCode, Gateway = config.Gateway, AccountNumber = config.AccountNumber,
            AccountName = config.AccountName, CreatedBy = userId, CreatedAt = now,
            ExpiresAt = now.AddMinutes(config.RequestLifetimeMinutes)
        };
        repository.Add(request);
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
        return Map(request, invoice, now);
    }

    public async Task<SePayPaymentRequestResponse> GetAsync(int userId, int invoiceId, Guid requestId, CancellationToken ct)
    {
        var config = Config;
        var invoice = await InvoiceAsync(userId, invoiceId, ct);
        var request = await repository.Query<SePayPaymentRequest>().AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == requestId && r.InvoiceId == invoiceId && r.Environment == config.Environment, ct)
            ?? throw new ClinicException(404, "Không tìm thấy yêu cầu thanh toán.");
        return Map(request, invoice, clock.GetUtcNow().UtcDateTime);
    }

    // Called ONLY after HMAC authentication of the raw body by the webhook controller.
    public async Task ReceiveAsync(SePayWebhookPayload payload, string payloadHash, CancellationToken ct)
    {
        var config = Config;
        var paidAt = ValidatePayload(payload, clock.GetUtcNow().UtcDateTime);
        await using var write = await repository.BeginWriteAsync(ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var previous = await repository.Query<SePayTransaction>().SingleOrDefaultAsync(t => t.Environment == config.Environment && t.ProviderTransactionId == payload.Id, ct);
        if (previous != null)
        {
            // Ignore JSON whitespace/property order. Conflicting economic data under the same ID is never silently accepted.
            if (previous.Gateway != payload.Gateway || previous.AccountNumber != payload.AccountNumber
                || previous.TransferType != payload.TransferType || previous.Amount != payload.TransferAmount
                || previous.Code != payload.Code || previous.Content != payload.Content
                || previous.ReferenceCode != payload.ReferenceCode || previous.TransactionAt != paidAt)
                throw new ClinicException(409, "ID giao dịch SePay đã nhận với nội dung khác. Cần kiểm tra đối soát.");
            return;
        }
        var receipt = new SePayTransaction
        {
            Environment = config.Environment, ProviderTransactionId = payload.Id, Gateway = payload.Gateway!,
            AccountNumber = payload.AccountNumber!, TransferType = payload.TransferType!, Amount = payload.TransferAmount,
            Code = payload.Code, Content = payload.Content!, ReferenceCode = payload.ReferenceCode,
            PayloadHash = payloadHash, TransactionAt = paidAt, ReceivedAt = now, Status = "ReviewRequired"
        };
        repository.Add(receipt);
        if (payload.AccountNumber != config.AccountNumber || !string.Equals(payload.Gateway, config.Gateway, StringComparison.OrdinalIgnoreCase))
            receipt.Reason = "UnexpectedAccount";
        else if (payload.TransferType == "out")
        {
            receipt.Status = "Ignored";
            receipt.Reason = "OutgoingTransfer";
        }
        else
        {
            var contentCodes = CodePattern.Matches(payload.Content!).Select(m => m.Value).Distinct(StringComparer.Ordinal).ToArray();
            var suppliedCode = payload.Code;
            var codeValid = !string.IsNullOrEmpty(suppliedCode) && suppliedCode.Length == 26 && CodePattern.IsMatch(suppliedCode);
            var code = codeValid ? suppliedCode : contentCodes.SingleOrDefaultIfOne();
            if (contentCodes.Length > 1 || (codeValid && contentCodes.Length == 1 && contentCodes[0] != suppliedCode))
                receipt.Reason = "AmbiguousCode";
            else if (code == null)
                receipt.Reason = "MissingCode";
            else
            {
                var request = await repository.Query<SePayPaymentRequest>().Include(r => r.Invoice).ThenInclude(i => i.Payments)
                    .SingleOrDefaultAsync(r => r.Code == code && r.Environment == config.Environment, ct);
                if (request == null) receipt.Reason = "UnknownCode";
                else
                {
                    receipt.PaymentRequest = request;
                    var remaining = Remaining(request.Invoice);
                    receipt.Reason = request.Status != "Pending" ? "RequestNotPending"
                        : request.ExpiresAt <= now ? "RequestExpired"
                        : paidAt >= request.ExpiresAt ? "TransferAfterExpiry"
                        : request.AccountNumber != payload.AccountNumber || !string.Equals(request.Gateway, payload.Gateway, StringComparison.OrdinalIgnoreCase) ? "AccountChanged"
                        : paidAt < request.CreatedAt.AddSeconds(-2) ? "TransferBeforeRequest"
                        : request.Invoice.Status != 0 || remaining <= 0 ? "InvoiceClosed"
                        : request.Amount != remaining ? "BalanceChanged"
                        : payload.TransferAmount < remaining ? "Underpaid"
                        : payload.TransferAmount > remaining ? "Overpaid" : null;
                    if (receipt.Reason == null)
                    {
                        var payment = new Payment
                        {
                            Invoice = request.Invoice, Amount = remaining, Method = 2, PaidAt = paidAt,
                            Provider = "SePay", ProviderEnvironment = config.Environment, ProviderTransactionId = payload.Id,
                            Note = "Chuyển khoản SePay " + payload.Id.ToString(CultureInfo.InvariantCulture)
                        };
                        repository.Add(payment);
                        receipt.Payment = payment;
                        receipt.Status = "Applied";
                        request.Invoice.Status = 1;
                        request.Status = "Paid";
                    }
                    else
                    {
                        if (request.Status is "Pending" or "Expired" or "Superseded") request.Status = "ReviewRequired";
                        // A delayed transfer to an old QR must also stop a newer pending QR on this invoice.
                        var otherPending = await repository.Query<SePayPaymentRequest>()
                            .Where(r => r.InvoiceId == request.InvoiceId && r.Environment == config.Environment && r.Status == "Pending").ToListAsync(ct);
                        foreach (var other in otherPending) other.Status = "ReviewRequired";
                    }
                }
            }
        }
        // Receipt, allocation and invoice update are atomic. Never ACK an unpersisted event.
        await repository.SaveAsync(ct);
        await write.CommitAsync(ct);
    }

    public async Task<SePayTransactionPage> TransactionsAsync(int userId, int page, string? status, CancellationToken ct)
    {
        var config = Config;
        if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được xem đối soát SePay.");
        if (page is < 1 or > 100000 || (status != null && status is not ("Applied" or "ReviewRequired" or "Ignored")))
            throw new ClinicException(400, "Bộ lọc giao dịch SePay không hợp lệ.");
        var query = repository.Query<SePayTransaction>().AsNoTracking().Where(t => t.Environment == config.Environment);
        if (status != null) query = query.Where(t => t.Status == status);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(t => t.Id).Skip((page - 1) * 20).Take(20)
            .Select(t => new SePayTransactionResponse(t.Id, t.ProviderTransactionId, t.Environment, t.Amount,
                DateTime.SpecifyKind(t.TransactionAt, DateTimeKind.Utc), DateTime.SpecifyKind(t.ReceivedAt, DateTimeKind.Utc),
                t.Status, t.Reason, t.PaymentRequestId, t.PaymentRequest == null ? null : (int?)t.PaymentRequest.InvoiceId, t.PaymentId)).ToListAsync(ct);
        return new(rows, page, 20, total);
    }

    private async Task<Invoice> InvoiceAsync(int userId, int invoiceId, CancellationToken ct)
    {
        var invoice = await repository.Query<Invoice>().Include(i => i.Payments).SingleOrDefaultAsync(i => i.Id == invoiceId, ct)
            ?? throw new ClinicException(404, "Không tìm thấy hóa đơn.");
        if (!await access.HasRoleAsync(userId, "Receptionist", ct) && !await access.HasRoleAsync(userId, "Admin", ct)
            && !await access.IsPatientOwnerAsync(userId, invoice.PatientId, ct))
            throw new ClinicException(403, "Không có quyền thanh toán hóa đơn.");
        return invoice;
    }

    private static decimal Remaining(Invoice invoice) => Math.Max(0, invoice.TotalAmount - invoice.Payments.Sum(p => p.Amount));
    private static SePayPaymentRequestResponse Map(SePayPaymentRequest request, Invoice invoice, DateTime now)
    {
        var remaining = Remaining(invoice);
        var status = request.Status;
        if (status == "Pending") status = invoice.Status == 2 ? "InvoiceCancelled"
            : invoice.Status == 1 ? "InvoiceSettled" : request.ExpiresAt <= now ? "Expired"
            : remaining != request.Amount ? "Superseded" : "Pending";
        var qr = status != "Pending" ? null : "https://vietqr.app/img?acc=" + Uri.EscapeDataString(request.AccountNumber)
            + "&bank=" + Uri.EscapeDataString(request.BankCode)
            + "&amount=" + request.Amount.ToString("0", CultureInfo.InvariantCulture) + "&des=" + request.Code;
        return new(request.Id, invoice.Id, request.Environment, request.Code, request.Amount,
            request.BankCode, request.AccountNumber, request.AccountName, DateTime.SpecifyKind(request.CreatedAt, DateTimeKind.Utc),
            DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Utc), status, qr, remaining);
    }

    private static DateTime ValidatePayload(SePayWebhookPayload p, DateTime now)
    {
        if (p.Id <= 0 || string.IsNullOrWhiteSpace(p.Gateway) || p.Gateway.Length > 64
            || string.IsNullOrWhiteSpace(p.AccountNumber) || p.AccountNumber.Length > 34
            || p.TransferType is not ("in" or "out") || p.Content == null || p.Content.Length > 2000
            || p.Code?.Length > 128 || p.ReferenceCode?.Length > 255
            || p.TransferAmount <= 0 || p.TransferAmount > 9999999999999999m || decimal.Truncate(p.TransferAmount) != p.TransferAmount
            || !DateTime.TryParseExact(p.TransactionDate, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || local.Year < 2000)
            throw new ClinicException(400, "Dữ liệu giao dịch SePay không hợp lệ.");
        var utc = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeSpan.FromHours(7)).UtcDateTime;
        if (utc > now.AddMinutes(5)) throw new ClinicException(400, "Thời điểm giao dịch SePay không hợp lệ.");
        return utc;
    }
}

internal static class SePayCodeSelection
{
    internal static string? SingleOrDefaultIfOne(this string[] codes) => codes.Length == 1 ? codes[0] : null;
}

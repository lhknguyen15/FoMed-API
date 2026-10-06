using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FoMed.Application.Services.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Payments;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

internal static class SePayAudit
{
    // Deliberately fake credentials, ONLY injected into the disposable local audit server.
    internal const string Secret = "FoMed-SePay-Audit-Only-Not-A-Real-Secret-2026";
    internal const string Account = "SEPAYTEST0001";
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }

    private static string Signature(string body, string timestamp, string secret = Secret) => "sha256=" +
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();

    internal static void RunUnitChecks()
    {
        var checks = 0;
        void Check(bool ok) { if (!ok) throw new Exception("SePay unit assertion failed"); checks++; }
        var config = new SePayOptions { Enabled = true, TestDatabaseName = "FoMed_SePay_Test", BankCode = "MB", Gateway = "MBBank", AccountNumber = Account, AccountName = "Audit", WebhookSecret = Secret };
        Check(config.IsValid());
        Check(!config.CanUseDatabase("FoMedDb")); Check(!config.CanUseDatabase("fomeddb"));
        Check(!config.CanUseDatabase("master")); Check(config.CanUseDatabase("FoMed_SePay_Test"));
        Check(!config.CanUseDatabase("AnotherDatabase"));
        config.TestDatabaseName = ""; Check(!config.IsValid()); Check(!config.CanUseDatabase("FoMedDb"));
        config.TestDatabaseName = "FoMedDb"; Check(config.IsValid()); Check(config.CanUseDatabase("FoMedDb")); Check(config.CanUseDatabase("fomeddb"));
        Check(!config.CanUseDatabase("master"));
        config.Environment = "Live"; Check(!config.IsValid()); config.AllowLivePayments = true; Check(config.IsValid()); config.Environment = "Test";
        config.Environment = "Live"; Check(!config.CanUseDatabase("FoMedDb")); Check(config.CanUseDatabase("FoMed_Live")); config.Environment = "Test";
        config.TestDatabaseName = "master"; Check(!config.IsValid()); Check(!config.CanUseDatabase("master"));
        config.TestDatabaseName = "FoMed_SePay_Test";
        config.WebhookSecret = "short"; Check(!config.IsValid()); config.WebhookSecret = Secret;
        config.AccountNumber = "bad&query"; Check(!config.IsValid()); config.AccountNumber = Account;
        config.SignatureToleranceSeconds = 3600; Check(!config.IsValid()); config.SignatureToleranceSeconds = 300;
        var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
        var timestamp = now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var body = "{\"content\":\"Tiếng Việt\",\"amount\":350000}";
        var authenticator = new SePayWebhookAuthenticator(Options.Create(config), new FixedClock(now));
        authenticator.Verify(Encoding.UTF8.GetBytes(body), timestamp, Signature(body, timestamp)); Check(true);
        foreach (var (raw, ts, signature) in new[] {
            (body + " ", timestamp, Signature(body, timestamp)),
            (body, timestamp, Signature(body, timestamp, "wrong-secret")),
            (body, "1799999699", Signature(body, "1799999699")),
            (body, "1800000301", Signature(body, "1800000301")),
            (body, "9223372036854775807", Signature(body, "9223372036854775807")),
            (body, timestamp, "sha256=" + new string('z', 64)),
            (body, timestamp, ""), (body, "not-a-timestamp", Signature(body, timestamp)) })
        {
            try { authenticator.Verify(Encoding.UTF8.GetBytes(raw), ts, signature); throw new Exception("Invalid HMAC accepted"); }
            catch (ClinicException e) { Check(e.StatusCode == 401); }
        }
        config.Enabled = false;
        try { authenticator.Verify(Encoding.UTF8.GetBytes(body), timestamp, Signature(body, timestamp)); throw new Exception("Disabled SePay accepted"); }
        catch (ClinicException e) { Check(e.StatusCode == 503); }
        Console.WriteLine($"PASS: {checks} SePay configuration/raw-body HMAC checks.");
    }

    internal static async Task RunAsync(DbContextOptions<FoMedDbContext> options, HttpClient client,
        Func<string?, string, string, object?, bool, Task<(int Status, JsonElement Body)>> call,
        Action<bool, string, object?> check)
    {
        void Check(bool ok, string label) => check(ok, "SePay: " + label, null);
        FoMedDbContext Db() => new(options);
        async Task<JsonElement> Need(string role, string method, string path, object? data = null)
        {
            var response = await call(role, method, path, data, false);
            Check(response.Status == 200, method + " " + path + " HTTP 200");
            if (response.Status != 200) throw new Exception("SePay HTTP audit stopped: " + response.Status);
            return response.Body.TryGetProperty("dataResponse", out var dataResponse) ? dataResponse : response.Body;
        }
        var invoiceCounter = 0;
        async Task<int> Invoice(decimal amount = 350000)
        {
            await using var db = Db();
            var patientId = await db.Patients.Where(p => p.User!.Username == "patient").Select(p => p.Id).SingleAsync();
            var cashierId = await db.Users.Where(u => u.Username == "receptionist").Select(u => u.Id).SingleAsync();
            var invoice = new Invoice { InvoiceNo = "SEPAY-AUDIT-" + (++invoiceCounter), PatientId = patientId,
                TotalAmount = amount, ConsultationFee = amount, CreatedBy = cashierId, CreatedAt = DateTime.UtcNow };
            db.Add(invoice); await db.SaveChangesAsync(); return invoice.Id;
        }
        async Task<JsonElement> Create(int invoice, string role = "receptionist") => await Need(role, "POST", $"/api/invoices/{invoice}/sepay/payment-requests");
        async Task<JsonElement> State(int invoice, JsonElement request, string role = "patient") => await Need(role, "GET", $"/api/invoices/{invoice}/sepay/payment-requests/{request.GetProperty("id").GetString()}");
        string Payload(long id, string? code, decimal amount = 350000, string account = Account, string type = "in", string? content = null, string gateway = "MBBank") => JsonSerializer.Serialize(new
        {
            id, gateway, transactionDate = DateTime.UtcNow.AddHours(7).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), accountNumber = account,
            code, content = content ?? (code ?? "khong co ma"), transferType = type, transferAmount = amount, referenceCode = "AUDIT-" + id
        });
        async Task<(int Status, string Body)> Webhook(string body, string? timestamp = null, string? signature = null, bool headers = true, string contentType = "application/json")
        {
            timestamp ??= DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/sepay");
            request.Content = new StringContent(body, Encoding.UTF8, contentType);
            if (headers) { request.Headers.Add("X-SePay-Timestamp", timestamp); request.Headers.Add("X-SePay-Signature", signature ?? Signature(body, timestamp)); }
            using var response = await client.SendAsync(request);
            return ((int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }
        async Task Receipt(long id, string status, string? reason)
        {
            await using var db = Db();
            var rows = await db.Set<SePayTransaction>().Where(t => t.Environment == "Test" && t.ProviderTransactionId == id).ToListAsync();
            Check(rows.Count == 1 && rows[0].Status == status && rows[0].Reason == reason, $"durable receipt {id}: {status}/{reason}");
        }
        var invoice = await Invoice();
        Check((await call(null, "POST", $"/api/invoices/{invoice}/sepay/payment-requests", null, false)).Status == 401, "create requires JWT");
        Check((await call("other-patient", "POST", $"/api/invoices/{invoice}/sepay/payment-requests", null, false)).Status == 403, "other patient cannot create");
        Check((await call("doctor", "POST", $"/api/invoices/{invoice}/sepay/payment-requests", null, false)).Status == 403, "doctor cannot create");
        var parallel = await Task.WhenAll(Create(invoice), Create(invoice, "patient"));
        var qr = parallel[0]; var code = qr.GetProperty("code").GetString()!;
        Check(qr.GetProperty("id").GetString() == parallel[1].GetProperty("id").GetString(), "concurrent create returns one pending request");
        Check(qr.GetProperty("amount").GetDecimal() == 350000 && code.Length == 26 && qr.GetProperty("environment").GetString() == "Test", "server amount/random reference/test label");
        Check(qr.GetProperty("qrUrl").GetString()!.Contains("amount=350000") && !qr.GetRawText().Contains(Secret), "QR amount is raw integer and response excludes secret");
        Check((await call("other-patient", "GET", $"/api/invoices/{invoice}/sepay/payment-requests/{qr.GetProperty("id").GetString()}", null, false)).Status == 403, "other patient cannot poll");
        Check((await State(invoice, qr)).GetProperty("status").GetString() == "Pending", "owner can poll pending state");
        Check((await Create(invoice, "admin")).GetProperty("id").GetString() == qr.GetProperty("id").GetString(), "admin reuses pending request");
        var body = Payload(1001, code);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        Check((await Webhook(body, headers: false)).Status == 401, "missing signature rejected");
        Check((await Webhook(body, timestamp, Signature(body, timestamp, "wrong"))).Status == 401, "wrong secret rejected");
        Check((await Webhook(body + " ", timestamp, Signature(body, timestamp))).Status == 401, "raw-body tampering rejected");
        var expired = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 301).ToString(CultureInfo.InvariantCulture);
        Check((await Webhook(body, expired)).Status == 401, "stale timestamp rejected");
        Check((await Webhook("not-json")).Status == 400, "signed malformed JSON rejected");
        Check((await Webhook(body[..^1] + ",\"Id\":999999}")).Status == 400, "duplicate/case-variant fields rejected");
        Check((await Webhook(body, contentType: "text/plain")).Status == 415, "non-JSON content rejected");
        Check((await Webhook(new string('x', 17000))).Status == 413, "oversized body rejected");
        Check((await Webhook(Payload(0, code))).Status == 400 && (await Webhook(Payload(999, code, 0.5m))).Status == 400, "invalid transaction ID and fractional VND rejected");
        await using (var db = Db()) Check(!await db.Set<SePayTransaction>().AnyAsync(), "unauthenticated/invalid requests create no receipts/payments");
        var delivered = await Task.WhenAll(Webhook(body), Webhook(body));
        Check(delivered.All(r => r.Status == 200 && JsonDocument.Parse(r.Body).RootElement.GetProperty("success").GetBoolean()), "concurrent duplicate webhooks both ACK using SePay contract");
        await Receipt(1001, "Applied", null);
        var paidInvoice = await Need("patient", "GET", $"/api/invoices/{invoice}");
        var payment = paidInvoice.GetProperty("payments").EnumerateArray().Single();
        Check(paidInvoice.GetProperty("status").GetInt32() == 1 && paidInvoice.GetProperty("paidAmount").GetDecimal() == 350000, "invoice settled once");
        Check(payment.GetProperty("provider").GetString() == "SePay" && payment.GetProperty("method").GetInt32() == 2 && payment.GetProperty("providerTransactionId").GetInt64() == 1001, "payment exposes source and provider ID");
        Check(payment.GetProperty("receivedBy").ValueKind == JsonValueKind.Null && payment.GetProperty("cashReceived").ValueKind == JsonValueKind.Null && payment.GetProperty("paidAt").GetString()!.EndsWith('Z'), "automatic transfer has no fake cashier/tender and UTC timestamp");
        Check((await State(invoice, qr)).GetProperty("status").GetString() == "Paid", "paid request survives reload");
        Check((await Webhook(body.Replace("350000", "350001"))).Status == 409, "same provider ID with different amount conflicts");
        Check((await Webhook(body.Replace(",", ", "))).Status == 200, "semantic duplicate tolerates JSON whitespace");
        Check((await call("patient", "POST", $"/api/invoices/{invoice}/sepay/payment-requests", null, false)).Status == 409, "paid invoice cannot create another QR");
        Check((await Webhook(Payload(1002, code))).Status == 200, "second actual transfer to paid QR ACKed for reconciliation");
        await Receipt(1002, "ReviewRequired", "RequestNotPending");
        foreach (var (id, amount, reason) in new[] { (1101L, 150000m, "Underpaid"), (1102L, 500000m, "Overpaid") })
        {
            var other = await Invoice(); var request = await Create(other);
            Check((await Webhook(Payload(id, request.GetProperty("code").GetString(), amount))).Status == 200, reason + " accepted into reconciliation ledger");
            await Receipt(id, "ReviewRequired", reason);
            var state = await State(other, request);
            Check(state.GetProperty("status").GetString() == "ReviewRequired" && state.GetProperty("qrUrl").ValueKind == JsonValueKind.Null, reason + " disables QR");
            Check((await call("receptionist", "POST", $"/api/invoices/{other}/sepay/payment-requests", null, false)).Status == 409, reason + " blocks new QR until reconciliation");
            Check((await Need("patient", "GET", $"/api/invoices/{other}")).GetProperty("paidAmount").GetDecimal() == 0, reason + " does not silently alter invoice revenue");
        }
        foreach (var (id, payload, status, reason) in new[] {
            (1201L, Payload(1201, code, account: "OTHERACCOUNT"), "ReviewRequired", "UnexpectedAccount"),
            (1202L, Payload(1202, code, type: "out"), "Ignored", "OutgoingTransfer"),
            (1203L, Payload(1203, null), "ReviewRequired", "MissingCode"),
            (1204L, Payload(1204, "FM" + new string('A', 24)), "ReviewRequired", "UnknownCode"),
            (1205L, Payload(1205, code, content: code + " FM" + new string('A', 24)), "ReviewRequired", "AmbiguousCode"),
            (1206L, Payload(1206, code, gateway: "OtherBank"), "ReviewRequired", "UnexpectedAccount") })
        {
            Check((await Webhook(payload)).Status == 200, reason + " durably ACKed"); await Receipt(id, status, reason);
        }
        var expiredInvoice = await Invoice(); var expiredRequest = await Create(expiredInvoice);
        await using (var db = Db())
        {
            var request = await db.Set<SePayPaymentRequest>().SingleAsync(r => r.InvoiceId == expiredInvoice);
            request.CreatedAt = DateTime.UtcNow.AddMinutes(-30); request.ExpiresAt = DateTime.UtcNow.AddMinutes(-5); await db.SaveChangesAsync();
        }
        Check((await State(expiredInvoice, expiredRequest)).GetProperty("status").GetString() == "Expired", "expired request cannot show active QR");
        var replacement = await Create(expiredInvoice);
        Check(replacement.GetProperty("id").GetString() != expiredRequest.GetProperty("id").GetString(), "expired request replaced with new code");
        Check((await Webhook(Payload(1301, expiredRequest.GetProperty("code").GetString()))).Status == 200, "late transfer to old code persisted");
        await Receipt(1301, "ReviewRequired", "RequestNotPending");
        Check((await State(expiredInvoice, replacement)).GetProperty("status").GetString() == "ReviewRequired", "late old transfer disables replacement QR");
        var cancelInvoice = await Invoice(); var canceledRequest = await Create(cancelInvoice);
        await Need("receptionist", "POST", $"/api/invoices/{cancelInvoice}/cancel", new { reason = "SePay audit" });
        Check((await State(cancelInvoice, canceledRequest)).GetProperty("status").GetString() == "InvoiceCancelled", "canceled invoice hides QR");
        Check((await Webhook(Payload(1302, canceledRequest.GetProperty("code").GetString()))).Status == 200, "transfer after cancellation persisted");
        await Receipt(1302, "ReviewRequired", "InvoiceClosed");
        var balanceInvoice = await Invoice(); var oldRequest = await Create(balanceInvoice);
        await Need("receptionist", "POST", $"/api/invoices/{balanceInvoice}/payments", new { amount = 100000, method = 0, cashReceived = 100000, idempotencyKey = Guid.NewGuid() });
        Check((await State(balanceInvoice, oldRequest)).GetProperty("status").GetString() == "Superseded", "partial/manual payment invalidates old amount");
        var newRequest = await Create(balanceInvoice);
        Check(newRequest.GetProperty("amount").GetDecimal() == 250000, "replacement QR uses authoritative debt");
        Check((await Webhook(Payload(1303, oldRequest.GetProperty("code").GetString()))).Status == 200, "transfer to superseded QR persisted");
        await Receipt(1303, "ReviewRequired", "RequestNotPending");
        var dueInvoice = await Invoice(); var dueRequest = await Create(dueInvoice);
        await using (var db = Db())
        {
            var request = await db.Set<SePayPaymentRequest>().SingleAsync(r => r.InvoiceId == dueInvoice);
            request.CreatedAt = DateTime.UtcNow.AddMinutes(-30); request.ExpiresAt = DateTime.UtcNow.AddMinutes(-5); await db.SaveChangesAsync();
        }
        Check((await Webhook(Payload(1304, dueRequest.GetProperty("code").GetString()))).Status == 200, "transfer after expiry before replacement persisted");
        await Receipt(1304, "ReviewRequired", "RequestExpired");
        var contentInvoice = await Invoice(); var contentRequest = await Create(contentInvoice);
        Check((await Webhook(Payload(1305, null, content: "Thanh toan " + contentRequest.GetProperty("code").GetString()))).Status == 200, "null provider code can match a single exact reference in content");
        await Receipt(1305, "Applied", null);
        var beforeInvoice = await Invoice(); var beforeRequest = await Create(beforeInvoice);
        var beforePayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Payload(1306, beforeRequest.GetProperty("code").GetString()))!;
        beforePayload["transactionDate"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.AddHours(7).AddMinutes(-5).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Check((await Webhook(JsonSerializer.Serialize(beforePayload))).Status == 200, "old bank transfer retained without applying to newly-created QR");
        await Receipt(1306, "ReviewRequired", "TransferBeforeRequest");
        var futureInvoice = await Invoice(); var futureRequest = await Create(futureInvoice);
        await using (var db = Db())
        {
            var request = await db.Set<SePayPaymentRequest>().SingleAsync(r => r.InvoiceId == futureInvoice);
            request.ExpiresAt = DateTime.UtcNow.AddMinutes(3); await db.SaveChangesAsync();
        }
        var futurePayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(Payload(1307, futureRequest.GetProperty("code").GetString()))!;
        futurePayload["transactionDate"] = JsonSerializer.SerializeToElement(DateTime.UtcNow.AddHours(7).AddMinutes(4).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        Check((await Webhook(JsonSerializer.Serialize(futurePayload))).Status == 200, "bank timestamp after request deadline cannot bypass expiry validation");
        await Receipt(1307, "ReviewRequired", "TransferAfterExpiry");
        var raceInvoice = await Invoice(); var raceRequest = await Create(raceInvoice);
        await Task.WhenAll(Webhook(Payload(1401, raceRequest.GetProperty("code").GetString())), Webhook(Payload(1402, raceRequest.GetProperty("code").GetString())));
        await using (var db = Db()) Check(await db.Payments.CountAsync(p => p.InvoiceId == raceInvoice) == 1
            && await db.Set<SePayTransaction>().CountAsync(t => t.ProviderTransactionId == 1401 || t.ProviderTransactionId == 1402) == 2, "two different transfers create one allocation and two receipts");
        var cashRaceInvoice = await Invoice(); var cashRaceQr = await Create(cashRaceInvoice);
        await Task.WhenAll(Webhook(Payload(1403, cashRaceQr.GetProperty("code").GetString())),
            call("receptionist", "POST", $"/api/invoices/{cashRaceInvoice}/payments", new { amount = 350000, method = 0, cashReceived = 500000, idempotencyKey = Guid.NewGuid() }, false));
        await using (var db = Db()) Check(await db.Payments.CountAsync(p => p.InvoiceId == cashRaceInvoice) == 1
            && await db.Payments.Where(p => p.InvoiceId == cashRaceInvoice).SumAsync(p => p.Amount) == 350000, "cash/webhook race never double-settles invoice");
        var fractionalInvoice = await Invoice(350000.5m);
        Check((await call("receptionist", "POST", $"/api/invoices/{fractionalInvoice}/sepay/payment-requests", null, false)).Status == 409, "fractional invoice is not silently rounded");
        var rollbackInvoice = await Invoice(); var rollbackRequest = await Create(rollbackInvoice); var rollbackBody = Payload(1501, rollbackRequest.GetProperty("code").GetString());
        await using (var db = Db()) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER billing.sepay_audit_fail ON billing.sepay_transactions AFTER INSERT AS BEGIN IF EXISTS (SELECT 1 FROM inserted WHERE provider_transaction_id = 1501) THROW 51009, 'Intentional SePay audit rollback', 1; END");
        try
        {
            Check((await Webhook(rollbackBody)).Status == 500, "database fault is not falsely ACKed");
            await using var db = Db();
            Check(!await db.Payments.AnyAsync(p => p.InvoiceId == rollbackInvoice) && !await db.Set<SePayTransaction>().AnyAsync(t => t.ProviderTransactionId == 1501)
                && await db.Invoices.AnyAsync(i => i.Id == rollbackInvoice && i.Status == 0)
                && await db.Set<SePayPaymentRequest>().AnyAsync(r => r.InvoiceId == rollbackInvoice && r.Status == "Pending"), "fault rolls back receipt/payment/invoice/request together");
        }
        finally { await using var db = Db(); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER billing.sepay_audit_fail"); }
        Check((await Webhook(rollbackBody)).Status == 200, "retry succeeds after transient DB fault");
        await Receipt(1501, "Applied", null);
        Check((await call("patient", "GET", "/api/sepay/transactions", null, false)).Status == 403, "patient cannot access reconciliation ledger");
        Check((await call("receptionist", "GET", "/api/sepay/transactions", null, false)).Status == 403, "cashier cannot access admin reconciliation ledger");
        var ledger = await Need("admin", "GET", "/api/sepay/transactions?status=ReviewRequired&page=1");
        Check(ledger.GetProperty("total").GetInt32() > 0 && ledger.GetProperty("pageSize").GetInt32() == 20 && !ledger.GetRawText().Contains(Secret)
            && !ledger.GetProperty("items")[0].TryGetProperty("content", out _), "admin ledger paginates without raw bank content/secrets");
        Check((await call("admin", "GET", "/api/sepay/transactions?page=0", null, false)).Status == 400, "invalid ledger page rejected");
        await using (var db = Db())
        {
            db.Payments.Add(new Payment { InvoiceId = invoice, Amount = 350000, Method = 2, PaidAt = DateTime.UtcNow,
                Provider = "SePay", ProviderEnvironment = "Test", ProviderTransactionId = 1001 });
            try { await db.SaveChangesAsync(); Check(false, "database unique index must reject duplicate provider payment"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627)
            { Check(true, "database unique index rejects duplicate provider payment"); }
        }
        await using (var db = Db())
        {
            var source = await db.Set<SePayTransaction>().AsNoTracking().SingleAsync(t => t.ProviderTransactionId == 1001);
            source.Id = 0; db.Add(source);
            try { await db.SaveChangesAsync(); Check(false, "database unique index must reject duplicate bank receipt"); }
            catch (DbUpdateException e) when (e.InnerException is SqlException sql && sql.Number is 2601 or 2627)
            { Check(true, "database unique index rejects duplicate bank receipt"); }
        }
        await using (var db = Db())
        {
            var migration = Regex.Replace(await File.ReadAllTextAsync("database/migrations/20261005_add_sepay_payments.sql"), @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (Regex.IsMatch(migration, @"\bUSE\s+|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase)) throw new Exception("Cross-database SQL prohibited.");
            foreach (var batch in Regex.Split(migration, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)) if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            Check(await db.Set<SePayTransaction>().AnyAsync(t => t.ProviderTransactionId == 1001 && t.Status == "Applied"), "migration rerun preserves receipts");
            var paymentsTotal = await db.Payments.SumAsync(p => p.Amount);
            var report = await Need("admin", "GET", "/api/reports/summary");
            Check(report.GetProperty("collectedAmount").GetDecimal() == paymentsTotal, "report includes only allocated payments, not unmatched/doubled bank receipts");
        }
        var wrongInvoice = await Invoice();
        Check((await call("patient", "GET", $"/api/invoices/{wrongInvoice}/sepay/payment-requests/{qr.GetProperty("id").GetString()}", null, false)).Status == 404, "request cannot be polled under another invoice");
    }
}

using System.Text.Json;
using System.Text.RegularExpressions;
using FoMed.Infrastructure.DbContext;
using FoMed.Infrastructure.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

// Called only with the disposable database and its isolated HTTP server.
internal static class PaymentCashAudit
{
    public static async Task RunAsync(DbContextOptions<FoMedDbContext> options,
        Func<string?, string, string, object?, bool, Task<(int Status, JsonElement Body)>> call,
        Action<bool, string, object?> check)
    {
        void Check(bool ok, string label) => check(ok, "Cash audit: " + label, null);
        async Task<JsonElement> Need(string role, string method, string path, object? body = null)
        {
            var response = await call(role, method, path, body, false);
            Check(response.Status == 200, method + " " + path + " returns 200");
            if (response.Status != 200) throw new Exception("Cash audit stopped: HTTP " + response.Status);
            return response.Body.GetProperty("dataResponse");
        }
        async Task Reject(string role, int id, object body, int expected, string label)
        {
            var response = await call(role, "POST", $"/api/invoices/{id}/payments", body, false);
            Check(response.Status == expected, label + ": HTTP " + expected);
        }
        int cashierId, patientId, invoiceId, partialId, legacyId, nonCashId;
        await using (var db = new FoMedDbContext(options))
        {
            cashierId = await db.Users.Where(u => u.Username == "receptionist").Select(u => u.Id).SingleAsync();
            patientId = await db.Patients.Where(p => p.User!.Username == "patient").Select(p => p.Id).SingleAsync();
            Invoice Invoice(string suffix) => new() { InvoiceNo = "CASH-AUDIT-" + suffix, PatientId = patientId, TotalAmount = 350000,
                ConsultationFee = 350000, CreatedBy = cashierId, CreatedAt = DateTime.UtcNow };
            var full = Invoice("full"); var partial = Invoice("partial"); var legacy = Invoice("legacy"); var other = Invoice("noncash");
            legacy.Payments.Add(new Payment { Amount = 150000, Method = 0, PaidAt = DateTime.UtcNow });
            db.AddRange(full, partial, legacy, other); await db.SaveChangesAsync();
            invoiceId = full.Id; partialId = partial.Id; legacyId = legacy.Id; nonCashId = other.Id;
        }
        var key = Guid.NewGuid();
        foreach (var invalid in new object[] {
            new { amount = 350000, method = 0, cashReceived = 349999 },
            new { amount = 350000, method = 0, cashReceived = 500000.5m },
            new { amount = 350000, method = 0, cashReceived = -1 },
            new { amount = 350000, method = 0, cashReceived = 10000000001m },
            new { amount = 350000, method = 1, cashReceived = 500000 },
            new { amount = 350000.001m, method = 0, cashReceived = 500000 },
            new { amount = 350000, method = 0, cashReceived = 500000, idempotencyKey = Guid.Empty },
            new { amount = 350000, method = 0, cashReceived = 500000, idempotencyKey = "not-a-guid" }
        }) await Reject("receptionist", invoiceId, invalid, 400, "Invalid tender/method/precision/key rejected");
        await Reject("patient", invoiceId, new { amount = 350000, method = 0, cashReceived = 500000, idempotencyKey = key }, 403, "Patient cannot record payment");
        await using (var db = new FoMedDbContext(options))
            Check(!await db.Payments.AnyAsync(p => p.InvoiceId == invoiceId), "All invalid requests leave no payment");

        object request = new { amount = 350000, method = 0, cashReceived = 500000, note = "  Cash audit  ", idempotencyKey = key,
            receivedBy = 999999, receivedByName = "SPOOFED", changeAmount = 0 };
        // Two concurrent equivalent requests, not merely two sequential successes.
        var both = await Task.WhenAll(call("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", request, true),
            call("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", request, false));
        Check(both.All(r => r.Status == 200), "Concurrent same-key requests both return the one committed transaction");
        if (both.Any(r => r.Status != 200)) throw new Exception("Concurrent payment audit failed");
        var settled = both[0].Body.GetProperty("dataResponse");
        var payment = settled.GetProperty("payments")[0];
        Check(settled.GetProperty("paidAmount").GetDecimal() == 350000 && settled.GetProperty("status").GetInt32() == 1 && settled.GetProperty("payments").GetArrayLength() == 1,
            "Applied amount is 350000, never the 500000 tender");
        Check(payment.GetProperty("cashReceived").GetDecimal() == 500000 && payment.GetProperty("changeAmount").GetDecimal() == 150000,
            "Server returns persisted tender and computed change");
        Check(payment.GetProperty("receivedBy").GetInt32() == cashierId && payment.GetProperty("receivedByName").GetString() == "Audit receptionist" && payment.GetProperty("idempotencyKey").GetGuid() == key,
            "Authenticated collector and name snapshot ignore caller spoofing");
        await Need("receptionist", "POST", $"/api/invoices/{invoiceId}/payments", request);
        await Reject("receptionist", invoiceId, new { amount = 350000, method = 0, cashReceived = 600000, note = "Cash audit", idempotencyKey = key }, 409, "Same key with different tender conflicts");
        await Reject("receptionist", invoiceId, new { amount = 349999, method = 0, cashReceived = 500000, note = "Cash audit", idempotencyKey = key }, 409, "Same key with different applied amount conflicts");
        await Reject("receptionist", invoiceId, new { amount = 350000, method = 0, cashReceived = 500000, note = "Changed note", idempotencyKey = key }, 409, "Same key with different note conflicts");
        await Reject("receptionist", invoiceId, new { amount = 350000, method = 1, note = "Cash audit", idempotencyKey = key }, 409, "Same key with different method conflicts");
        await Reject("receptionist", partialId, request, 409, "Same key cannot pay another invoice");
        await Reject("receptionist", invoiceId, new { amount = 350000, method = 0, cashReceived = 500000, idempotencyKey = Guid.NewGuid() }, 409, "New key cannot collect settled invoice again");
        var own = await Need("patient", "GET", $"/api/invoices/{invoiceId}");
        Check(own.GetProperty("payments")[0].GetProperty("changeAmount").GetDecimal() == 150000, "Owner can reload confirmed cash details");
        Check(own.GetProperty("payments")[0].GetProperty("paidAt").GetString()!.EndsWith('Z'), "Reloaded payment timestamp has explicit UTC timezone");
        var forbidden = await call("other-patient", "GET", $"/api/invoices/{invoiceId}", null, false);
        Check(forbidden.Status == 403, "Another patient cannot view cash details");
        await using (var db = new FoMedDbContext(options))
        {
            Check(await db.Payments.CountAsync(p => p.InvoiceId == invoiceId) == 1, "Concurrent requests plus replay persist exactly one row");
            var actor = await db.Users.SingleAsync(u => u.Id == cashierId);
            actor.FullName = new string('A', 255); await db.SaveChangesAsync();
        }
        try
        {
            var historical = await Need("receptionist", "GET", $"/api/invoices/{invoiceId}");
            Check(historical.GetProperty("payments")[0].GetProperty("receivedByName").GetString() == "Audit receptionist", "Old collector name survives account edit");
            var partial = await Need("receptionist", "POST", $"/api/invoices/{partialId}/payments", new { amount = 150000, method = 0, cashReceived = 200000, idempotencyKey = Guid.NewGuid() });
            Check(partial.GetProperty("status").GetInt32() == 0 && partial.GetProperty("remainingAmount").GetDecimal() == 200000 && partial.GetProperty("payments")[0].GetProperty("changeAmount").GetDecimal() == 50000,
                "Partial-payment API remains supported independently of tender");
            Check(partial.GetProperty("payments")[0].GetProperty("receivedByName").GetString()!.Length == 255, "Full-length account name snapshot fits schema");
        }
        finally
        {
            await using var db = new FoMedDbContext(options);
            var actor = await db.Users.SingleAsync(u => u.Id == cashierId); actor.FullName = "Audit receptionist"; await db.SaveChangesAsync();
        }
        var maximumTender = await Need("receptionist", "POST", $"/api/invoices/{partialId}/payments", new { amount = 200000, method = 0, cashReceived = 10000000000m, idempotencyKey = Guid.NewGuid() });
        Check(maximumTender.GetProperty("payments")[1].GetProperty("changeAmount").GetDecimal() == 9999800000m && maximumTender.GetProperty("paidAmount").GetDecimal() == 350000, "Maximum tender does not overflow or inflate revenue");
        var old = await Need("patient", "GET", $"/api/invoices/{legacyId}");
        Check(new[] { "cashReceived", "changeAmount", "receivedBy", "receivedByName", "idempotencyKey" }.All(field => old.GetProperty("payments")[0].GetProperty(field).ValueKind == JsonValueKind.Null),
            "Legacy payment is unknown, not inferred from invoice creator or amount");
        var oldClient = await Need("receptionist", "POST", $"/api/invoices/{legacyId}/payments", new { amount = 200000, method = 0 });
        Check(oldClient.GetProperty("payments")[1].GetProperty("cashReceived").ValueKind == JsonValueKind.Null && oldClient.GetProperty("payments")[1].GetProperty("receivedBy").GetInt32() == cashierId,
            "Old clients without tender/key still work and record authenticated cashier");
        var nonCash = await Need("admin", "POST", $"/api/invoices/{nonCashId}/payments", new { amount = 350000, method = 1, idempotencyKey = key });
        Check(nonCash.GetProperty("payments")[0].GetProperty("idempotencyKey").GetGuid() == key, "Retry key is scoped to authenticated collector, not shared globally");
        Check(nonCash.GetProperty("payments")[0].GetProperty("cashReceived").ValueKind == JsonValueKind.Null && nonCash.GetProperty("payments")[0].GetProperty("changeAmount").ValueKind == JsonValueKind.Null && nonCash.GetProperty("payments")[0].GetProperty("receivedByName").GetString() == "Audit admin",
            "Noncash payment has collector but no fictional tender/change");

        await using (var db = new FoMedDbContext(options))
        {
            var sql = await File.ReadAllTextAsync("database/migrations/20261005_add_payment_cash_audit.sql");
            sql = Regex.Replace(sql, @"^\s*USE\s+\[?FoMedDb\]?\s*;\s*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (Regex.IsMatch(sql, @"\bUSE\s+|\b(?:CREATE|ALTER|DROP)\s+DATABASE\b", RegexOptions.IgnoreCase)) throw new Exception("Cross-database SQL is prohibited.");
            for (var run = 0; run < 2; run++)
                foreach (var batch in Regex.Split(sql, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
                    if (!string.IsNullOrWhiteSpace(batch)) await db.Database.ExecuteSqlRawAsync(batch);
            var legacy = await db.Payments.AsNoTracking().Where(p => p.InvoiceId == legacyId).OrderBy(p => p.Id).FirstAsync();
            Check(legacy.CashReceived == null && legacy.ReceivedBy == null && legacy.ReceivedByNameSnapshot == null && legacy.IdempotencyKey == null,
                "Migration reruns safely without backfilling historical rows");
            async Task RejectedSql(FormattableString statement, string label)
            {
                try { await db.Database.ExecuteSqlInterpolatedAsync(statement); Check(false, label); }
                catch (SqlException e) when (e.Number is 547 or 2601 or 2627) { Check(true, label); }
            }
            await RejectedSql($"INSERT INTO billing.payments(invoice_id, amount, method, cash_received, received_by, idempotency_key) VALUES ({partialId}, 1, 0, 1, {cashierId}, {key})", "Unique index blocks duplicate authenticated actor/key");
            await RejectedSql($"INSERT INTO billing.payments(invoice_id, amount, method, cash_received) VALUES ({partialId}, 100, 0, 99)", "Database rejects tender smaller than payment");
            await RejectedSql($"INSERT INTO billing.payments(invoice_id, amount, method, cash_received) VALUES ({partialId}, 100, 1, 200)", "Database rejects tender on noncash payment");
            await RejectedSql($"INSERT INTO billing.payments(invoice_id, amount, method, cash_received) VALUES ({partialId}, 100, 0, 200.5)", "Database rejects fractional cash tender");
        }
    }
}

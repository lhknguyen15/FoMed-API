namespace FoMed.Application.DTO.Billing;

public sealed record SePayPaymentRequestResponse(Guid Id, int InvoiceId, string Environment,
    string Code, decimal Amount, string BankCode, string AccountNumber, string AccountName,
    DateTime CreatedAt, DateTime ExpiresAt, string Status, string? QrUrl, decimal RemainingAmount);

// SePay serializes these names in camelCase. Never accept an invoice ID or actor from this payload.
public sealed record SePayWebhookPayload
{
    public long Id { get; init; }
    public string? Gateway { get; init; }
    public string? TransactionDate { get; init; }
    public string? AccountNumber { get; init; }
    public string? Code { get; init; }
    public string? Content { get; init; }
    public string? TransferType { get; init; }
    public decimal TransferAmount { get; init; }
    public string? ReferenceCode { get; init; }
}

public sealed record SePayTransactionResponse(long Id, long ProviderTransactionId, string Environment,
    decimal Amount, DateTime TransactionAt, DateTime ReceivedAt, string Status, string? Reason,
    Guid? PaymentRequestId, int? InvoiceId, int? PaymentId);
public sealed record SePayTransactionPage(IReadOnlyList<SePayTransactionResponse> Items, int Page, int PageSize, int Total);

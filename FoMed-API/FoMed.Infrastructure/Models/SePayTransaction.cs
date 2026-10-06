namespace FoMed.Infrastructure.Models;

// A durable receipt, including transfers which cannot be automatically allocated to an invoice.
public sealed class SePayTransaction
{
    public long Id { get; set; }
    public string Environment { get; set; } = null!;
    public long ProviderTransactionId { get; set; }
    public string Gateway { get; set; } = null!;
    public string AccountNumber { get; set; } = null!;
    public string TransferType { get; set; } = null!;
    public decimal Amount { get; set; }
    public string? Code { get; set; }
    public string Content { get; set; } = null!;
    public string? ReferenceCode { get; set; }
    public string PayloadHash { get; set; } = null!;
    public DateTime TransactionAt { get; set; }
    public DateTime ReceivedAt { get; set; }
    public Guid? PaymentRequestId { get; set; }
    public int? PaymentId { get; set; }
    public string Status { get; set; } = null!;
    public string? Reason { get; set; }
    public SePayPaymentRequest? PaymentRequest { get; set; }
    public Payment? Payment { get; set; }
}

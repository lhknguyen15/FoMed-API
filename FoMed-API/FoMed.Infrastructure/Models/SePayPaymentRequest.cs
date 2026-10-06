namespace FoMed.Infrastructure.Models;

public sealed class SePayPaymentRequest
{
    public Guid Id { get; set; }
    public int InvoiceId { get; set; }
    public string Environment { get; set; } = null!;
    public string Code { get; set; } = null!;
    public decimal Amount { get; set; }
    public string BankCode { get; set; } = null!;
    public string Gateway { get; set; } = null!;
    public string AccountNumber { get; set; } = null!;
    public string AccountName { get; set; } = null!;
    public int CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "Pending";
    public Invoice Invoice { get; set; } = null!;
}

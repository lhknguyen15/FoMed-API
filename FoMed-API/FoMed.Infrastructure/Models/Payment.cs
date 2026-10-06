using System;
using System.Collections.Generic;

namespace FoMed.Infrastructure.Models;

public partial class Payment
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public decimal Amount { get; set; }

    public byte Method { get; set; }

    public DateTime PaidAt { get; set; }

    public string? Note { get; set; }

    public decimal? CashReceived { get; set; }

    public int? ReceivedBy { get; set; }

    public string? ReceivedByNameSnapshot { get; set; }

    public Guid? IdempotencyKey { get; set; }

    public string? Provider { get; set; }

    public string? ProviderEnvironment { get; set; }

    public long? ProviderTransactionId { get; set; }

    public virtual User? ReceivedByUser { get; set; }

    public virtual Invoice Invoice { get; set; } = null!;
}

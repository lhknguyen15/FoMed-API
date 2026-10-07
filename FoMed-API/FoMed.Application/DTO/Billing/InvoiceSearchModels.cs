using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Billing;

public sealed record InvoiceSearchRequest
{
    [MaxLength(100)] public string? Keyword { get; init; }
    [MaxLength(20)] public string Status { get; init; } = "all";
    public DateOnly? FromDate { get; init; }
    public DateOnly? ToDate { get; init; }
    [Range(1, 100000)] public int Page { get; init; } = 1;
}

public sealed record InvoiceSummaryResponse(int Id, string InvoiceNo, int PatientId, string PatientCode,
    string PatientName, int? MedicalRecordId, DateTime CreatedAt, decimal TotalAmount, decimal PaidAmount, byte Status);

public sealed record InvoiceSearchResponse(IReadOnlyList<InvoiceSummaryResponse> Items, int Page, int PageSize, int TotalCount);

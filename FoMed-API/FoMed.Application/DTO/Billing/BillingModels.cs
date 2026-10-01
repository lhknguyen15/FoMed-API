using System.ComponentModel.DataAnnotations;
namespace FoMed.Application.DTO.Billing;

public sealed record CreateInvoiceRequest
{
    [Range(1, int.MaxValue)] public int MedicalRecordId { get; init; }
}
public sealed record RecordPaymentRequest
{
    [Range(typeof(decimal), "0.01", "9999999999.99")] public decimal Amount { get; init; }
    [Range(0, 3)] public byte Method { get; init; }
    [MaxLength(255)] public string? Note { get; init; }
}
public sealed record InvoiceLineResponse(string? Description, int Quantity, decimal UnitPrice, decimal Amount);
public sealed record PaymentResponse(int Id, decimal Amount, byte Method, DateTime PaidAt);
public sealed record InvoiceResponse(int Id, string InvoiceNo, int PatientId, int? MedicalRecordId,
    decimal TotalAmount, decimal PaidAmount, byte Status, IReadOnlyList<InvoiceLineResponse> Items, IReadOnlyList<PaymentResponse> Payments,
    decimal ConsultationFee)
{
    public decimal RemainingAmount => Math.Max(TotalAmount - PaidAmount, 0m);
    public string StatusName => Status switch
    {
        1 => "Đã thanh toán",
        2 => "Đã hủy",
        _ when PaidAmount > 0 => "Thanh toán một phần",
        _ => "Chưa thanh toán"
    };
}

public sealed record InvoiceCandidateResponse(
    int MedicalRecordId,
    int AppointmentId,
    int PatientId,
    string PatientName,
    DateTime AppointmentStartTime,
    decimal ConsultationFee,
    decimal ServiceAndMedicineAmount,
    decimal EstimatedTotalAmount);

public sealed record CancelInvoiceRequest
{
    [MaxLength(500)] public string? Reason { get; init; }
}

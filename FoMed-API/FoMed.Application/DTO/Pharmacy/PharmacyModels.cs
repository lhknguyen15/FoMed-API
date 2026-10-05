using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Pharmacy;

public sealed record InventoryBatchResponse(
    int BatchId, int MedicineId, string MedicineName, string? Unit, string LotNumber,
    DateOnly ExpiryDate, int Quantity, decimal UnitPrice, bool IsExpired);

public sealed record ReceiveStockRequest
{
    [Range(1, int.MaxValue)] public int MedicineId { get; init; }
    [Required, MaxLength(50)] public string LotNumber { get; init; } = "";
    public DateOnly ExpiryDate { get; init; }
    [Range(1, int.MaxValue)] public int Quantity { get; init; }
}

public sealed record AdjustStockRequest
{
    [Range(1, int.MaxValue)] public int BatchId { get; init; }
    [Range(-1000000, 1000000)] public int Quantity { get; init; }
    [Required, MaxLength(255)] public string Reason { get; init; } = "";
}

public sealed record StockTransactionResponse(long Id, int BatchId, byte Type, int Quantity, string? RefType, int? RefId, DateTime CreatedAt);

public sealed record ReceiveStockReceiptRequest
{
    [Required, MaxLength(255)] public string SupplierName { get; init; } = "";
    [Required, MaxLength(100)] public string DocumentNo { get; init; } = "";
    [MaxLength(500)] public string? Note { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<ReceiveStockLineRequest> Items { get; init; } = [];
}

public sealed record ReceiveStockLineRequest
{
    [Range(1, int.MaxValue)] public int MedicineId { get; init; }
    [Required, MaxLength(50)] public string LotNumber { get; init; } = "";
    public DateOnly ExpiryDate { get; init; }
    [Range(1, int.MaxValue)] public int Quantity { get; init; }
    [Range(typeof(decimal), "0", "9999999999.99")] public decimal UnitCost { get; init; }
}

public sealed record InventoryReceiptResponse(int Id, string SupplierName, string DocumentNo, DateTime ReceivedAt,
    decimal TotalAmount, IReadOnlyList<InventoryReceiptLineResponse> Items);

public sealed record InventoryReceiptLineResponse(int MedicineId, string MedicineName, string LotNumber,
    DateOnly ExpiryDate, int Quantity, decimal UnitCost, decimal LineAmount);

public sealed record DispensePrescriptionResponse(int PrescriptionId, DateTime DispensedAt, bool AlreadyDispensed,
    IReadOnlyList<DispensedLineResponse> Lines);

public sealed record DispensedLineResponse(int PrescriptionItemId, int MedicineId, string MedicineName, int BatchId,
    string LotNumber, int Quantity, DateOnly ExpiryDate);

public sealed record PharmacyPrescriptionResponse(int PrescriptionId, int MedicalRecordId,
    string PatientName, string PatientCode, string DoctorName, bool IsFinalized, byte AppointmentStatus,
    bool IsDispensed, bool IsFullyDispensed, bool CanDispense, string? BlockedReason,
    IReadOnlyList<PharmacyPrescriptionLineResponse> Items);

public sealed record PharmacyPrescriptionLineResponse(int MedicineId, string MedicineName,
    int Quantity, int DispensedQuantity, string? Dosage, string? Instruction)
{
    public string? Unit { get; init; }
    public long AvailableQuantity { get; init; }
    public int RemainingQuantity { get; init; }
    public int ShortageQuantity { get; init; }
    public IReadOnlyList<PharmacyBatchProposal> ProposedBatches { get; init; } = [];
}
public sealed record PharmacyBatchProposal(int BatchId, string LotNumber, DateOnly ExpiryDate, int AvailableQuantity, int ProposedQuantity);
public sealed record PharmacyPrescriptionListItem(int PrescriptionId, int MedicalRecordId, string PatientName,
    string PatientCode, string DoctorName, DateTime CreatedAt, bool IsFullyDispensed);
public sealed record PharmacyPrescriptionPage(IReadOnlyList<PharmacyPrescriptionListItem> Items, int Page, int PageSize, int Total);

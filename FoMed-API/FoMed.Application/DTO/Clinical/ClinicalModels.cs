using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Clinical;

public sealed record SaveMedicalRecordRequest
{
    [MaxLength(2000)] public string? Symptoms { get; init; }
    [MaxLength(2000)] public string? Diagnosis { get; init; }
    [MaxLength(2000)] public string? Note { get; init; }
    public VitalSignsRequest? VitalSigns { get; init; }
    [MaxLength(20)] public string? Icd10Code { get; init; }
    [MaxLength(4000)] public string? TreatmentPlan { get; init; }
    public DateOnly? FollowUpDate { get; init; }
}
public sealed record VitalSignsRequest
{
    [Range(20, 300)] public decimal? Systolic { get; init; }
    [Range(10, 200)] public decimal? Diastolic { get; init; }
    [Range(20, 250)] public decimal? HeartRate { get; init; }
    [Range(25, 45)] public decimal? Temperature { get; init; }
    [Range(0.1, 500)] public decimal? WeightKg { get; init; }
    [Range(0.1, 300)] public decimal? HeightCm { get; init; }
}
public sealed record CreatePrescriptionRequest
{
    [MaxLength(500)] public string? Note { get; init; }
    public bool AllergyAcknowledged { get; init; }
    [Required, MinLength(1), MaxLength(100)] public List<PrescriptionLineRequest> Items { get; init; } = [];
}
public sealed record PrescriptionLineRequest
{
    [Range(1, int.MaxValue)] public int MedicineId { get; init; }
    [Range(1, 100000)] public int Quantity { get; init; }
    [Required, MaxLength(255)] public string Dosage { get; init; } = "";
    [MaxLength(255)] public string? Instruction { get; init; }
}
public sealed record OrderServiceRequest
{
    [Range(1, int.MaxValue)] public int ServiceId { get; init; }
    [Range(1, 1000)] public int Quantity { get; init; } = 1;
}
public sealed record SaveLabResultRequest
{
    [Required, MaxLength(2000)] public string ResultSummary { get; init; } = "";
    [MaxLength(1000)] public string? Conclusion { get; init; }
    [MaxLength(1000)] public string? ReferenceRange { get; init; }
}
public sealed record MedicalRecordResponse(int Id, int AppointmentId, int PatientId, int DoctorId,
    string? Symptoms, string? Diagnosis, string? Note, VitalSignsRequest? VitalSigns, string? Icd10Code,
    string? TreatmentPlan, DateOnly? FollowUpDate, bool IsFinalized, DateTime? FinalizedAt,
    DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record PrescriptionLineResponse(int MedicineId, string MedicineName, int Quantity, decimal UnitPriceSnapshot, string? Dosage, string? Instruction);
public sealed record PrescriptionResponse(int Id, int MedicalRecordId, string? Note, IReadOnlyList<PrescriptionLineResponse> Items)
{
    public bool IsDispensed { get; init; }
}
public sealed record ServiceOrderResponse(int Id, int MedicalRecordId, int ServiceId, string ServiceName, byte Status,
    int Quantity, decimal UnitPriceSnapshot, string? ResultSummary, string? Conclusion, string? ReferenceRange, DateTime? ResultAt);
public sealed record CatalogResponse(int Id, string Name, decimal Price);
public sealed record PrescribingMedicineResponse(int Id, string Name, string? Unit, decimal Price, long AvailableQuantity);
public sealed record PrescribingContextResponse(int MedicalRecordId, string PatientName, string? Allergies,
    IReadOnlyList<PrescribingMedicineResponse> Medicines);
public sealed record MedicineSearchResponse(IReadOnlyList<PrescribingMedicineResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record LabResultHistoryItem(ServiceOrderResponse Order, string PatientName, string PatientCode);
public sealed record LabResultHistoryPage(IReadOnlyList<LabResultHistoryItem> Items, int Page, int PageSize, int Total);

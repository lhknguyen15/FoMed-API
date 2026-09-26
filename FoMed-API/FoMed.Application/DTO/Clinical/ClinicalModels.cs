using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Clinical;

public sealed record SaveMedicalRecordRequest
{
    [MaxLength(2000)] public string? Symptoms { get; init; }
    [MaxLength(2000)] public string? Diagnosis { get; init; }
    [MaxLength(2000)] public string? Note { get; init; }
}
public sealed record CreatePrescriptionRequest
{
    [MaxLength(500)] public string? Note { get; init; }
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
}
public sealed record SaveLabResultRequest
{
    [Required, MaxLength(2000)] public string ResultSummary { get; init; } = "";
    [MaxLength(1000)] public string? Conclusion { get; init; }
}
public sealed record MedicalRecordResponse(int Id, int AppointmentId, int PatientId, int DoctorId,
    string? Symptoms, string? Diagnosis, string? Note, DateTime CreatedAt, DateTime? UpdatedAt);
public sealed record PrescriptionLineResponse(int MedicineId, string MedicineName, int Quantity, string? Dosage, string? Instruction);
public sealed record PrescriptionResponse(int Id, int MedicalRecordId, string? Note, IReadOnlyList<PrescriptionLineResponse> Items);
public sealed record ServiceOrderResponse(int Id, int MedicalRecordId, int ServiceId, string ServiceName, byte Status,
    string? ResultSummary, string? Conclusion, DateTime? ResultAt);
public sealed record CatalogResponse(int Id, string Name, decimal Price);

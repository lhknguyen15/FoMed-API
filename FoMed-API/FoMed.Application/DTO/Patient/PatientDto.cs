namespace FoMed.Application.DTO.Patient;

// DTO theo Review ERD cho GET /api/patients/lookup.
public sealed record PatientDto(
    int PatientId,
    string FullName,
    DateOnly? DateOfBirth,
    bool HasUserAccount);

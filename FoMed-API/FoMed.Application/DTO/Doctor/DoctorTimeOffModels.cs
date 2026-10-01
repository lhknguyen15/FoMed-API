using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Doctor;

public sealed record DoctorTimeOffResponse(int Id, int? DoctorId, DateTime StartAt, DateTime EndAt, string? Reason);

public sealed record SaveDoctorTimeOffRequest
{
    public int? DoctorId { get; init; }
    public DateTime StartAt { get; init; }
    public DateTime EndAt { get; init; }
    [MaxLength(255)] public string? Reason { get; init; }
}

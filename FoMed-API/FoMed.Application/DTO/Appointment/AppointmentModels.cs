using System.ComponentModel.DataAnnotations;
using FoMed.Infrastructure.Models.Enums;

namespace FoMed.Application.DTO.Appointment;

public sealed record AvailableSlotResponse(
    DateTime StartTime,
    DateTime EndTime,
    bool IsAvailable,
    string? UnavailableReason = null);

public sealed record BookAppointmentRequest
{
    [Required]
    public int DoctorId { get; init; }

    [Required]
    public DateTime StartTime { get; init; }

    [Range(1, int.MaxValue)]
    public int? ServiceId { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record StaffBookAppointmentRequest
{
    [Range(1, int.MaxValue)] public int PatientId { get; init; }
    [Range(1, int.MaxValue)] public int DoctorId { get; init; }
    [Required] public DateTime StartTime { get; init; }
    [Range(1, int.MaxValue)] public int? ServiceId { get; init; }
    [Range(1, 2)] public byte Source { get; init; } = 1; // 1: phone, 2: walk-in
    [MaxLength(500)] public string? Reason { get; init; }
}

public sealed record ChangeAppointmentStatusRequest
{
    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record QueueActionRequest
{
    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record CancelAppointmentRequest
{
    [Required, MinLength(3), MaxLength(500)]
    public string Reason { get; init; } = string.Empty;
}

public sealed record RescheduleAppointmentRequest
{
    [Required]
    public DateTime StartTime { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record AppointmentResponse(
    int Id,
    string AppointmentCode,
    int PatientId,
    string PatientName,
    string? PatientPhone,
    int DoctorId,
    string DoctorName,
    string DoctorSpecialty,
    string? DoctorRoom,
    DateTime StartTime,
    DateTime EndTime,
    AppointmentStatus Status,
    string StatusName,
    string? Reason,
    int? QueueNumber,
    DateTime CreatedAt,
    int? ServiceId,
    string? ServiceName,
    DateTime? CheckedInAt,
    byte Source,
    decimal? FeeSnapshot);

public sealed record AppointmentStatusHistoryResponse(
    int Id,
    int AppointmentId,
    byte? FromStatus,
    string? FromStatusName,
    byte ToStatus,
    string ToStatusName,
    string? ChangedByName,
    string? Reason,
    DateTime ChangedAt);

public sealed record DoctorQueuePatientResponse(
    AppointmentResponse Appointment,
    string? Allergies,
    IReadOnlyList<PatientHistorySummary> RecentHistory);

public sealed record PatientHistorySummary(
    int MedicalRecordId,
    int AppointmentId,
    DateTime VisitAt,
    string? Diagnosis,
    string? Note);

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

    [MaxLength(500)]
    public string? Reason { get; init; }
}

public sealed record ChangeAppointmentStatusRequest
{
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
    DateTime CreatedAt);

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
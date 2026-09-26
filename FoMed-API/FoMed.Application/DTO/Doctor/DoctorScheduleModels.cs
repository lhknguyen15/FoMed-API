using System.ComponentModel.DataAnnotations;

namespace FoMed.Application.DTO.Doctor;

public sealed record DoctorScheduleResponse(
    int Id,
    int DoctorId,
    byte DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    int SlotMinutes,
    bool IsActive);

public sealed record SaveDoctorScheduleRequest
{
    [Range(0, 6)]
    public byte DayOfWeek { get; init; }

    public TimeOnly StartTime { get; init; }

    public TimeOnly EndTime { get; init; }

    [Range(5, 240)]
    public int SlotMinutes { get; init; } = 30;
}

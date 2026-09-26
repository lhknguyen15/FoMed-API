namespace FoMed.Application.Services.Appointment;

// Existing SQL scheduling values are Vietnam wall-clock time, without an offset.
// Audit timestamps remain UTC. Explicit UTC/offset inputs are converted to UTC+07.
public static class ClinicTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);
    public static DateTime Now => DateTime.SpecifyKind(DateTime.UtcNow.Add(Offset), DateTimeKind.Unspecified);
    public static DateTime Normalize(DateTime value) => value.Kind == DateTimeKind.Unspecified
        ? value : DateTime.SpecifyKind(value.ToUniversalTime().Add(Offset), DateTimeKind.Unspecified);

    public static bool IsSlot(DateTime start, TimeOnly shiftStart, TimeOnly shiftEnd, int minutes)
    {
        if (minutes <= 0 || shiftEnd <= shiftStart) return false;
        var date = DateOnly.FromDateTime(start);
        var first = date.ToDateTime(shiftStart);
        var last = date.ToDateTime(shiftEnd);
        return start >= first && start <= last &&
            (start - first).Ticks % TimeSpan.FromMinutes(minutes).Ticks == 0 &&
            (last - start).TotalMinutes >= minutes;
    }
}

namespace FoMed.Infrastructure.Models.Enums;

public enum AppointmentStatus : byte
{
    Pending = 0,
    Confirmed = 1,
    InProgress = 2,
    Completed = 3,
    Cancelled = 4,
    NoShow = 5
}
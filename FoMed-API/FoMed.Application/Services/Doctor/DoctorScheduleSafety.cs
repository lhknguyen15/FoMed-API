using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Doctor;

// Call inside the same WriteScope as booking and shift updates.
public static class DoctorScheduleSafety
{
    public static bool Covers(DoctorSchedule shift, FoMed.Infrastructure.Models.Appointment visit) =>
        shift.IsActive && shift.DoctorId == visit.DoctorId && shift.DayOfWeek == (byte)visit.StartTime.DayOfWeek &&
        ClinicTime.IsSlot(visit.StartTime, shift.StartTime, shift.EndTime, shift.SlotMinutes) &&
        visit.EndTime == visit.StartTime.AddMinutes(shift.SlotMinutes);

    public static async Task EnsureAsync(ClinicRepository repository, DoctorSchedule original, DoctorSchedule replacement, CancellationToken ct)
    {
        if (!original.IsActive) return;
        var today = ClinicTime.Now.Date;
        var visits = await repository.Query<FoMed.Infrastructure.Models.Appointment>().AsNoTracking()
            .Where(a => a.DoctorId == original.DoctorId && (a.Status == 2 || (a.Status < 2 && a.StartTime >= today)))
            .ToListAsync(ct);
        // Include unfinished visits from earlier days; completed/cancelled history is never rewritten.
        var affected = visits.Where(a => (byte)a.StartTime.DayOfWeek == original.DayOfWeek &&
            TimeOnly.FromDateTime(a.StartTime) < original.EndTime && TimeOnly.FromDateTime(a.EndTime) > original.StartTime).ToList();
        if (affected.Count == 0) return;
        var remaining = await repository.Query<DoctorSchedule>().AsNoTracking()
            .Where(s => s.DoctorId == original.DoctorId && s.IsActive && s.Id != original.Id).ToListAsync(ct);
        remaining.Add(replacement);
        var blocked = affected.Count(a => !remaining.Any(s => Covers(s, a)));
        if (blocked > 0) throw new ClinicException(409,
            $"Không thể thay đổi ca vì có {blocked} lịch hẹn từ hôm nay hoặc lượt đang khám bị ảnh hưởng. Hãy xử lý các lịch hẹn này trước; hệ thống không tự chuyển hoặc hủy lịch.");
    }
}

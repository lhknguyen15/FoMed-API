using System.Globalization;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = FoMed.Infrastructure.Models.Appointment;

namespace FoMed.Application.Services.Billing;

public sealed class ReportService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<ReportSummaryResponse> SummaryAsync(int userId, ReportQuery request, CancellationToken ct)
    {
        await RequireReportAccessAsync(userId, ct); var (from, to) = NormalizeRange(request);
        var appointments = await repository.Query<AppointmentEntity>().AsNoTracking().Include(a => a.Doctor).Where(a => a.StartTime >= from && a.StartTime < to && (!request.DoctorId.HasValue || a.DoctorId == request.DoctorId.Value)).ToListAsync(ct);
        var appointmentIds = appointments.Select(a => a.Id).ToArray();
        var invoiceFromUtc = from.AddHours(-ClinicTime.Offset.TotalHours); var invoiceToUtc = to.AddHours(-ClinicTime.Offset.TotalHours);
        var invoices = await repository.Query<Invoice>().AsNoTracking().Include(i => i.Payments).Where(i => i.CreatedAt >= invoiceFromUtc && i.CreatedAt < invoiceToUtc && i.Status != 2 && (!request.DoctorId.HasValue || (i.Appointment != null && i.Appointment.DoctorId == request.DoctorId.Value))).ToListAsync(ct);
        var doctors = appointments.GroupBy(a => new { a.DoctorId, Name = a.Doctor.FullName }).Select(g => new DoctorReportRow(g.Key.DoctorId, g.Key.Name, g.Count(), g.Count(a => a.Status == 3), g.Count(a => a.Status == 5), invoices.Where(i => i.AppointmentId.HasValue && g.Select(a => a.Id).Contains(i.AppointmentId.Value)).Sum(i => i.TotalAmount), invoices.Where(i => i.AppointmentId.HasValue && g.Select(a => a.Id).Contains(i.AppointmentId.Value)).SelectMany(i => i.Payments).Sum(p => p.Amount))).OrderBy(x => x.DoctorName).ToList();
        var invoiced = invoices.Sum(i => i.TotalAmount); var collected = invoices.SelectMany(i => i.Payments).Sum(p => p.Amount);
        return new ReportSummaryResponse(from, to, appointments.Count, appointments.Count(a => a.Status == 3), appointments.Count(a => a.Status == 5), appointments.Count(a => a.Status == 4), invoiced, collected, Math.Max(0m, invoiced - collected), doctors);
    }
    public async Task<(byte[] Content, string FileName)> ExportCsvAsync(int userId, ReportQuery request, CancellationToken ct)
    {
        var report = await SummaryAsync(userId, request, ct); using var writer = new StringWriter(CultureInfo.InvariantCulture); writer.WriteLine("DoctorId,DoctorName,AppointmentCount,CompletedCount,NoShowCount,InvoicedAmount,CollectedAmount"); foreach (var row in report.Doctors) writer.WriteLine($"{row.DoctorId},\"{row.DoctorName.Replace("\"", "\"\"")}\",{row.AppointmentCount},{row.CompletedCount},{row.NoShowCount},{row.InvoicedAmount.ToString(CultureInfo.InvariantCulture)},{row.CollectedAmount.ToString(CultureInfo.InvariantCulture)}"); return (System.Text.Encoding.UTF8.GetBytes(writer.ToString()), $"fomed-report-{report.From:yyyyMMdd}-{report.To.AddDays(-1):yyyyMMdd}.csv");
    }
    private static (DateTime From, DateTime To) NormalizeRange(ReportQuery request)
    {
        var now = ClinicTime.Now; var from = ClinicTime.Normalize(request.From ?? now.Date.AddDays(-30)); var to = ClinicTime.Normalize(request.To ?? now.Date.AddDays(1)); if (to <= from || (to - from).TotalDays > 366) throw new ClinicException(400, "Khoảng thời gian báo cáo không hợp lệ."); return (from, to);
    }
    private async Task RequireReportAccessAsync(int userId, CancellationToken ct) { if (!await access.HasRoleAsync(userId, "Admin", ct) && !await access.HasRoleAsync(userId, "Receptionist", ct)) throw new ClinicException(403, "KhÃ´ng cÃ³ quyá»n xem bÃ¡o cÃ¡o."); }
}

using System.Globalization;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Appointment;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using AppointmentEntity = FoMed.Infrastructure.Models.Appointment;
using DoctorEntity = FoMed.Infrastructure.Models.Doctor;

namespace FoMed.Application.Services.Billing;

public sealed class ReportService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<ReportSummaryResponse> SummaryAsync(int userId, ReportQuery request, CancellationToken ct)
    {
        await RequireReportAccessAsync(userId, ct);
        var (from, to) = NormalizeRange(request);
        var utcFrom = from.AddHours(-ClinicTime.Offset.TotalHours);
        var utcTo = to.AddHours(-ClinicTime.Offset.TotalHours);

        // Appointment schedule columns use Vietnam wall-clock time, while invoice/payment timestamps are UTC.
        var appointmentsQuery = repository.Query<AppointmentEntity>().AsNoTracking()
            .Where(a => a.StartTime >= from && a.StartTime < to);
        var issuedInvoicesQuery = repository.Query<Invoice>().AsNoTracking()
            .Where(i => i.CreatedAt >= utcFrom && i.CreatedAt < utcTo && i.Status != 2);
        var collectedPaymentsQuery = repository.Query<Payment>().AsNoTracking()
            .Where(p => p.PaidAt >= utcFrom && p.PaidAt < utcTo && p.Invoice.Status != 2);
        // Only open invoices are queried for receivables, so the ERD's (status, created_at) index applies.
        IQueryable<Invoice> receivablesQuery = repository.Query<Invoice>().AsNoTracking()
            .Where(i => i.Status == 0 && i.CreatedAt >= utcFrom && i.CreatedAt < utcTo)
            .Include(i => i.Payments);

        if (request.DoctorId.HasValue)
        {
            var doctorId = request.DoctorId.Value;
            appointmentsQuery = appointmentsQuery.Where(a => a.DoctorId == doctorId);
            issuedInvoicesQuery = issuedInvoicesQuery.Where(i => i.Appointment != null && i.Appointment.DoctorId == doctorId);
            collectedPaymentsQuery = collectedPaymentsQuery.Where(p => p.Invoice.Appointment != null && p.Invoice.Appointment.DoctorId == doctorId);
            receivablesQuery = receivablesQuery.Where(i => i.Appointment != null && i.Appointment.DoctorId == doctorId);
        }

        var appointments = await appointmentsQuery.ToListAsync(ct);
        var issuedInvoices = await issuedInvoicesQuery
            .Select(i => new { i.TotalAmount, DoctorId = i.Appointment == null ? (int?)null : i.Appointment.DoctorId })
            .ToListAsync(ct);
        var collectedPayments = await collectedPaymentsQuery
            .Select(p => new { p.Amount, DoctorId = p.Invoice.Appointment == null ? (int?)null : p.Invoice.Appointment.DoctorId })
            .ToListAsync(ct);
        var receivableInvoices = await receivablesQuery
            .Select(i => new { i.TotalAmount, PaymentsTotal = i.Payments.Sum(p => p.Amount), DoctorId = i.Appointment == null ? (int?)null : i.Appointment.DoctorId })
            .ToListAsync(ct);

        var appointmentGroups = appointments.GroupBy(a => a.DoctorId).ToDictionary(g => g.Key, g => new
        {
            Count = g.Count(),
            Completed = g.Count(a => a.Status == 3),
            NoShow = g.Count(a => a.Status == 5),
            Cancelled = g.Count(a => a.Status == 4)
        });
        var doctorIds = appointmentGroups.Keys
            .Concat(issuedInvoices.Where(x => x.DoctorId.HasValue).Select(x => x.DoctorId!.Value))
            .Concat(collectedPayments.Where(x => x.DoctorId.HasValue).Select(x => x.DoctorId!.Value))
            .Concat(receivableInvoices.Where(x => x.DoctorId.HasValue).Select(x => x.DoctorId!.Value))
            .Distinct().ToArray();

        // A payment today may belong to an older visit. Resolve names independently of visits in this period.
        // Include inactive doctors in historical reports and fetch only IDs/names in one batch (not per row).
        var doctorNames = doctorIds.Length == 0 ? new Dictionary<int, string>() : await repository.Query<DoctorEntity>()
            .AsNoTracking().Where(d => doctorIds.Contains(d.Id))
            .Select(d => new { d.Id, d.FullName })
            .ToDictionaryAsync(d => d.Id, d => d.FullName, ct);

        var doctors = doctorIds.Select(doctorId =>
        {
            appointmentGroups.TryGetValue(doctorId, out var appointmentGroup);
            var issued = issuedInvoices.Where(x => x.DoctorId == doctorId).Sum(x => x.TotalAmount);
            var collected = collectedPayments.Where(x => x.DoctorId == doctorId).Sum(x => x.Amount);
            var outstanding = receivableInvoices.Where(x => x.DoctorId == doctorId).Sum(x => Math.Max(0m, x.TotalAmount - x.PaymentsTotal));
            var appointmentCount = appointmentGroup?.Count ?? 0;
            var cancelledCount = appointmentGroup?.Cancelled ?? 0;
            var denominator = appointmentCount - cancelledCount;
            var noShowCount = appointmentGroup?.NoShow ?? 0;
            var noShowRate = denominator == 0 ? 0m : decimal.Round(noShowCount * 100m / denominator, 1);
            var doctorName = doctorNames.GetValueOrDefault(doctorId);
            return new DoctorReportRow(doctorId, string.IsNullOrWhiteSpace(doctorName) ? "Chưa có thông tin bác sĩ" : doctorName, appointmentCount,
                appointmentGroup?.Completed ?? 0, noShowCount, cancelledCount, noShowRate, issued, collected, outstanding);
        }).OrderBy(x => x.DoctorName).ToList();

        var totalAppointments = appointments.Count;
        var noShowAppointments = appointments.Count(a => a.Status == 5);
        var cancelledAppointments = appointments.Count(a => a.Status == 4);
        var noShowDenominator = totalAppointments - cancelledAppointments;
        var noShowRatePercent = noShowDenominator == 0 ? 0m : decimal.Round(noShowAppointments * 100m / noShowDenominator, 1);

        return new ReportSummaryResponse(from, to, totalAppointments,
            appointments.Count(a => a.Status == 3), noShowAppointments, cancelledAppointments,
            noShowRatePercent, issuedInvoices.Sum(x => x.TotalAmount), collectedPayments.Sum(x => x.Amount),
            receivableInvoices.Sum(x => Math.Max(0m, x.TotalAmount - x.PaymentsTotal)), doctors);
    }

    public async Task<(byte[] Content, string FileName)> ExportCsvAsync(int userId, ReportQuery request, CancellationToken ct)
    {
        var report = await SummaryAsync(userId, request, ct);
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        writer.WriteLine("DoctorId,DoctorName,AppointmentCount,CompletedCount,NoShowCount,CancelledCount,NoShowRatePercent,InvoicedAmount,RevenueCollected,OutstandingAmount");
        writer.WriteLine($",Total,{report.TotalAppointments},{report.CompletedAppointments},{report.NoShowAppointments},{report.CancelledAppointments},{report.NoShowRatePercent.ToString(CultureInfo.InvariantCulture)},{report.InvoicedAmount.ToString(CultureInfo.InvariantCulture)},{report.CollectedAmount.ToString(CultureInfo.InvariantCulture)},{report.OutstandingAmount.ToString(CultureInfo.InvariantCulture)}");
        foreach (var row in report.Doctors)
        {
            writer.WriteLine($"{row.DoctorId},\"{row.DoctorName.Replace("\"", "\"\"")}\",{row.AppointmentCount},{row.CompletedCount},{row.NoShowCount},{row.CancelledCount},{row.NoShowRatePercent.ToString(CultureInfo.InvariantCulture)},{row.InvoicedAmount.ToString(CultureInfo.InvariantCulture)},{row.CollectedAmount.ToString(CultureInfo.InvariantCulture)},{row.OutstandingAmount.ToString(CultureInfo.InvariantCulture)}");
        }
        return (System.Text.Encoding.UTF8.GetBytes(writer.ToString()), $"fomed-report-{report.From:yyyyMMdd}-{report.To.AddDays(-1):yyyyMMdd}.csv");
    }

    private static (DateTime From, DateTime To) NormalizeRange(ReportQuery request)
    {
        var now = ClinicTime.Now;
        var from = ClinicTime.Normalize(request.From ?? now.Date.AddDays(-30));
        var to = ClinicTime.Normalize(request.To ?? now.Date.AddDays(1));
        if (to <= from || (to - from).TotalDays > 366) throw new ClinicException(400, "Khoảng thời gian báo cáo không hợp lệ.");
        return (from, to);
    }

    private async Task RequireReportAccessAsync(int userId, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Admin", ct) && !await access.HasRoleAsync(userId, "Receptionist", ct))
            throw new ClinicException(403, "Không có quyền xem báo cáo.");
    }
}

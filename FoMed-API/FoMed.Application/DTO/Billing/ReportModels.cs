namespace FoMed.Application.DTO.Billing;

public sealed record ReportSummaryResponse(DateTime From, DateTime To, int TotalAppointments, int CompletedAppointments, int NoShowAppointments, int CancelledAppointments, decimal NoShowRatePercent, decimal InvoicedAmount, decimal CollectedAmount, decimal OutstandingAmount, IReadOnlyList<DoctorReportRow> Doctors);
public sealed record DoctorReportRow(int DoctorId, string DoctorName, int AppointmentCount, int CompletedCount, int NoShowCount, int CancelledCount, decimal NoShowRatePercent, decimal InvoicedAmount, decimal CollectedAmount, decimal OutstandingAmount);
public sealed record ReportQuery(DateTime? From, DateTime? To, int? DoctorId);

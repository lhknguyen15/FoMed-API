namespace FoMed.Application.DTO.Billing;

public sealed record ReportSummaryResponse(DateTime From, DateTime To, int TotalAppointments, int CompletedAppointments, int NoShowAppointments, int CancelledAppointments, decimal InvoicedAmount, decimal CollectedAmount, decimal OutstandingAmount, IReadOnlyList<DoctorReportRow> Doctors);
public sealed record DoctorReportRow(int DoctorId, string DoctorName, int AppointmentCount, int CompletedCount, int NoShowCount, decimal InvoicedAmount, decimal CollectedAmount);
public sealed record ReportQuery(DateTime? From, DateTime? To, int? DoctorId);

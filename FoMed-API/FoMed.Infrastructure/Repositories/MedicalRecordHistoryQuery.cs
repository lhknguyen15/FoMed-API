using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;

namespace FoMed.Infrastructure.Repositories;

public static class MedicalRecordHistoryQuery
{
    // Shared by queue preview and record history; filter BEFORE ordering/limiting.
    public static IQueryable<MedicalRecord> Recent(IQueryable<MedicalRecord> records,
        int patientId, int currentAppointmentId, DateTime visitAt) => records
        .Where(r => r.PatientId == patientId && r.Appointment.PatientId == patientId
            && r.AppointmentId != currentAppointmentId && r.IsFinalized
            && r.Appointment.Status == (byte)AppointmentStatus.Completed
            && r.Appointment.StartTime < visitAt)
        .OrderByDescending(r => r.Appointment.StartTime).ThenByDescending(r => r.Id)
        .Take(5);
}

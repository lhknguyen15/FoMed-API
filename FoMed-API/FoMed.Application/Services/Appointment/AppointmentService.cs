using FoMed.Application.DTO;
using FoMed.Application.DTO.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.UnitOfWork;

namespace FoMed.Application.Services.Appointment;

public sealed class AppointmentService(IUnitOfWork unitOfWork)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    // 1. Xem lich trong cua Bac si theo ngay
    public async Task<HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlotsAsync(
        int doctorId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var doctor = await _unitOfWork.DoctorRepository.GetByIdAsync(doctorId, cancellationToken);
        if (doctor == null || !doctor.IsActive)
            return new HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>
            {
                DataResponse = Array.Empty<AvailableSlotResponse>(),
                Message = AppointmentResponseMessageDTO.DoctorNotFound,
                StatusCode = 404
            };

        var dayOfWeek = (byte)date.DayOfWeek;
        var schedules = await _unitOfWork.DoctorScheduleRepository.GetByDoctorIdAsync(doctorId, cancellationToken);
        var daySchedules = schedules.Where(s => s.DayOfWeek == dayOfWeek && s.IsActive).ToList();

        if (daySchedules.Count == 0)
            return new HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>
            {
                DataResponse = Array.Empty<AvailableSlotResponse>(),
                Message = "Bac si khong co lich lam viec vao ngay nay.",
                StatusCode = 200
            };

        var slots = new List<AvailableSlotResponse>();
        var now = ClinicTime.Now;

        foreach (var schedule in daySchedules)
        {
            var slotMinutes = schedule.SlotMinutes > 0 ? schedule.SlotMinutes : 30;
            var slotStart = date.ToDateTime(schedule.StartTime);
            var scheduleEnd = date.ToDateTime(schedule.EndTime);

            while (slotStart.AddMinutes(slotMinutes) <= scheduleEnd)
            {
                var slotEnd = slotStart.AddMinutes(slotMinutes);
                bool isAvailable = true;
                string? reason = null;

                if (slotStart <= now)
                {
                    isAvailable = false;
                    reason = "Khung gio da qua.";
                }
                else if (await _unitOfWork.AppointmentRepository.IsDoctorOnTimeOffAsync(doctorId, slotStart, slotEnd, cancellationToken))
                {
                    isAvailable = false;
                    reason = "Bac si nghi trong thoi gian nay.";
                }
                else if (await _unitOfWork.AppointmentRepository.HasDoctorConflictAsync(doctorId, slotStart, slotEnd, null, cancellationToken))
                {
                    isAvailable = false;
                    reason = "Da co benh nhan dat.";
                }

                slots.Add(new AvailableSlotResponse(slotStart, slotEnd, isAvailable, reason));
                slotStart = slotEnd;
            }
        }

        return new HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>
        {
            DataResponse = slots,
            Message = AppointmentResponseMessageDTO.GetAvailableSlotsSuccess,
            StatusCode = 200
        };
    }

    // 2. Benh nhan dat lich kham
    public async Task<HTTPResponseData<AppointmentResponse?>> BookAppointmentAsync(
        int currentUserId, BookAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (patient == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientNotFound, 404);
        if (!patient.IsActive)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientInactive, 400);

        var doctor = await _unitOfWork.DoctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
        if (doctor == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorNotFound, 404);
        if (!doctor.IsActive)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorInactive, 400);

        if (request.ServiceId.HasValue &&
            await _unitOfWork.ServiceRepository.GetActiveByIdAsync(request.ServiceId.Value, cancellationToken) is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.ServiceNotFound, 404);

        request = request with { StartTime = ClinicTime.Normalize(request.StartTime) };
        if (request.StartTime <= ClinicTime.Now)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PastTimeNotAllowed, 400);

        var dayOfWeek = (byte)request.StartTime.DayOfWeek;
        var schedules = await _unitOfWork.DoctorScheduleRepository.GetByDoctorIdAsync(request.DoctorId, cancellationToken);

        var matchingSchedule = schedules.FirstOrDefault(s =>
            s.DayOfWeek == dayOfWeek && s.IsActive &&
            ClinicTime.IsSlot(request.StartTime, s.StartTime, s.EndTime, s.SlotMinutes));

        if (matchingSchedule == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorNoSchedule, 400);

        var slotMinutes = matchingSchedule.SlotMinutes > 0 ? matchingSchedule.SlotMinutes : 30;
        var endTime = request.StartTime.AddMinutes(slotMinutes);

        if (await _unitOfWork.AppointmentRepository.IsDoctorOnTimeOffAsync(request.DoctorId, request.StartTime, endTime, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorTimeOffOverlap, 400);

        if (await _unitOfWork.AppointmentRepository.HasDoctorConflictAsync(request.DoctorId, request.StartTime, endTime, null, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorConflict, 409);

        if (await _unitOfWork.AppointmentRepository.HasPatientConflictAsync(patient.Id, request.StartTime, endTime, null, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientConflict, 409);

        var dateOnly = DateOnly.FromDateTime(request.StartTime);
        var code = await _unitOfWork.AppointmentRepository.GenerateCodeAsync(cancellationToken);
        var queue = await _unitOfWork.AppointmentRepository.GetNextQueueNumberAsync(request.DoctorId, dateOnly, cancellationToken);

        var appointment = new FoMed.Infrastructure.Models.Appointment
        {
            AppointmentCode = code,
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ServiceId = request.ServiceId,
            StartTime = request.StartTime,
            EndTime = endTime,
            Status = (byte)AppointmentStatus.Pending,
            QueueNumber = queue,
            Source = 0,
            Reason = request.Reason,
            CreatedBy = currentUserId,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.AppointmentRepository.AddAsync(appointment, cancellationToken);

        var history = new AppointmentStatusHistory
        {
            Appointment = appointment,
            FromStatus = null,
            ToStatus = (byte)AppointmentStatus.Pending,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Dat lich qua he thong online",
            ChangedAt = DateTime.UtcNow
        };
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(history, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        var created = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointment.Id, cancellationToken);
        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(created!),
            Message = AppointmentResponseMessageDTO.BookSuccess,
            StatusCode = 201
        };
    }

    // 3. Benh nhan xem lich hen cua minh
    public async Task<HTTPResponseData<IReadOnlyList<AppointmentResponse>>> GetPatientAppointmentsAsync(
        int currentUserId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (patient == null)
            return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
            {
                DataResponse = Array.Empty<AppointmentResponse>(),
                Message = AppointmentResponseMessageDTO.PatientNotFound,
                StatusCode = 404
            };

        var list = await _unitOfWork.AppointmentRepository.GetPatientAppointmentsAsync(patient.Id, date, status, cancellationToken);
        return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
        {
            DataResponse = list.Select(MapToResponse).ToList(),
            Message = AppointmentResponseMessageDTO.GetListSuccess,
            StatusCode = 200
        };
    }

    // 4. Bac si xem lich hen cua minh
    public async Task<HTTPResponseData<IReadOnlyList<AppointmentResponse>>> GetDoctorAppointmentsAsync(
        int currentUserId, DateOnly? date = null, AppointmentStatus? status = null, CancellationToken cancellationToken = default)
    {
        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (doctor == null)
            return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
            {
                DataResponse = Array.Empty<AppointmentResponse>(),
                Message = AppointmentResponseMessageDTO.DoctorNotFound,
                StatusCode = 404
            };

        var list = await _unitOfWork.AppointmentRepository.GetDoctorAppointmentsAsync(doctor.Id, date, status, cancellationToken);
        return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
        {
            DataResponse = list.Select(MapToResponse).ToList(),
            Message = AppointmentResponseMessageDTO.GetListSuccess,
            StatusCode = 200
        };
    }

    // 5. Xem chi tiet lich hen
    public async Task<HTTPResponseData<AppointmentResponse?>> GetAppointmentByIdAsync(
        int currentUserId, int appointmentId, CancellationToken cancellationToken = default)
    {
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);

        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if ((patient == null || patient.Id != appointment.PatientId) &&
            (doctor == null || doctor.Id != appointment.DoctorId))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.GetAppointmentSuccess,
            StatusCode = 200
        };
    }

    // 6. Xac nhan lich hen (Pending -> Confirmed)
    public async Task<HTTPResponseData<AppointmentResponse?>> ConfirmAppointmentAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanChangeStatusAsync(currentUserId, appointment.DoctorId, true, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.Pending)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.InvalidStatusTransition, 400);

        var old = appointment.Status;
        appointment.Status = (byte)AppointmentStatus.Confirmed;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = old,
            ToStatus = (byte)AppointmentStatus.Confirmed,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Bac si / Le tan xac nhan lich hen",
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.ConfirmSuccess,
            StatusCode = 200
        };
    }

    // 7. Hoan thanh ca kham (Confirmed/InProgress -> Completed)
    public async Task<HTTPResponseData<AppointmentResponse?>> CompleteAppointmentAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanChangeStatusAsync(currentUserId, appointment.DoctorId, false, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.Confirmed &&
            appointment.Status != (byte)AppointmentStatus.InProgress)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.InvalidStatusTransition, 400);

        var old = appointment.Status;
        appointment.Status = (byte)AppointmentStatus.Completed;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = old,
            ToStatus = (byte)AppointmentStatus.Completed,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Bac si da hoan thanh ca kham",
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.CompleteSuccess,
            StatusCode = 200
        };
    }

    // 8. Huy lich hen
    public async Task<HTTPResponseData<AppointmentResponse?>> CancelAppointmentAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);

        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if ((patient == null || patient.Id != appointment.PatientId) &&
            (doctor == null || doctor.Id != appointment.DoctorId))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        if (appointment.Status == (byte)AppointmentStatus.Completed ||
            appointment.Status == (byte)AppointmentStatus.Cancelled ||
            appointment.Status == (byte)AppointmentStatus.NoShow)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.InvalidStatusTransition, 400);

        var old = appointment.Status;
        appointment.Status = (byte)AppointmentStatus.Cancelled;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = old,
            ToStatus = (byte)AppointmentStatus.Cancelled,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Huy lich hen",
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.CancelSuccess,
            StatusCode = 200
        };
    }

    // 9. Xem lich su trang thai
    public async Task<HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>> GetStatusHistoryAsync(
        int currentUserId, int appointmentId, CancellationToken cancellationToken = default)
    {
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return new HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>
            {
                DataResponse = Array.Empty<AppointmentStatusHistoryResponse>(),
                Message = AppointmentResponseMessageDTO.AppointmentNotFound,
                StatusCode = 404
            };

        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if ((patient == null || patient.Id != appointment.PatientId) &&
            (doctor == null || doctor.Id != appointment.DoctorId))
            return new HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>
            {
                DataResponse = Array.Empty<AppointmentStatusHistoryResponse>(),
                Message = AppointmentResponseMessageDTO.UnauthorizedAccess,
                StatusCode = 403
            };

        var histories = await _unitOfWork.AppointmentRepository.GetStatusHistoryAsync(appointmentId, cancellationToken);
        return new HTTPResponseData<IReadOnlyList<AppointmentStatusHistoryResponse>>
        {
            DataResponse = histories.Select(h => new AppointmentStatusHistoryResponse(
                h.Id, h.AppointmentId, h.FromStatus,
                h.FromStatus.HasValue ? ((AppointmentStatus)h.FromStatus.Value).ToString() : null,
                h.ToStatus, ((AppointmentStatus)h.ToStatus).ToString(),
                h.ChangedByNavigation?.FullName, h.Reason, h.ChangedAt
            )).ToList(),
            Message = AppointmentResponseMessageDTO.GetListSuccess,
            StatusCode = 200
        };
    }

    // Kiểm tra role thực tế và bác sĩ phụ trách, kể cả khi service được gọi ngoài controller.
    private async Task<bool> CanChangeStatusAsync(
        int currentUserId, int doctorId, bool allowReceptionist, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken);
        if (user is null || !user.IsActive)
            return false;

        if (allowReceptionist && user.UserRoles.Any(role => role.Role.Name == "Receptionist"))
            return true;

        if (!user.UserRoles.Any(role => role.Role.Name == "Doctor"))
            return false;

        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        return doctor is { IsActive: true } && doctor.Id == doctorId;
    }

    private static HTTPResponseData<T> Fail<T>(string message, int statusCode) where T : class? =>
        new() { DataResponse = default!, Message = message, StatusCode = statusCode };

    private static AppointmentResponse MapToResponse(FoMed.Infrastructure.Models.Appointment a)
    {
        var status = (AppointmentStatus)a.Status;
        return new AppointmentResponse(
            a.Id, a.AppointmentCode,
            a.PatientId, a.Patient.FullName, a.Patient.Phone,
            a.DoctorId, a.Doctor.FullName,
            a.Doctor.Specialty?.Name ?? "Chua xep", a.Doctor.Room,
            a.StartTime, a.EndTime,
            status, status.ToString(),
            a.Reason, a.QueueNumber, a.CreatedAt,
            a.ServiceId, a.Service?.Name);
    }
}

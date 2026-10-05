using FoMed.Application.DTO;
using FoMed.Application.DTO.Appointment;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Models.Enums;
using FoMed.Infrastructure.UnitOfWork;
using FoMed.Application.Services.Clinical;

namespace FoMed.Application.Services.Appointment;

public sealed class AppointmentService(IUnitOfWork unitOfWork, IClinicalAuditContext? auditContext = null)
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private static readonly TimeSpan PatientChangeCutoff = TimeSpan.FromHours(24);

    // 1. Xem lich trong cua Bac si theo ngay
    public async Task<HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>> GetAvailableSlotsAsync(
        int doctorId, DateOnly date, CancellationToken cancellationToken = default, int? serviceId = null)
    {
        var doctor = await _unitOfWork.DoctorRepository.GetByIdAsync(doctorId, cancellationToken);
        if (doctor == null || !doctor.IsActive)
            return new HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>
            {
                DataResponse = Array.Empty<AvailableSlotResponse>(),
                Message = AppointmentResponseMessageDTO.DoctorNotFound,
                StatusCode = 404
            };

        if (serviceId.HasValue &&
            await _unitOfWork.ServiceRepository.GetActiveByIdAsync(serviceId.Value, cancellationToken) is null)
            return new HTTPResponseData<IReadOnlyList<AvailableSlotResponse>>
            {
                DataResponse = Array.Empty<AvailableSlotResponse>(),
                Message = AppointmentResponseMessageDTO.ServiceNotFound,
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
        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (patient == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientNotFound, 404);
        if (!patient.IsActive)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientInactive, 400);

        return await BookAppointmentForPatientCoreAsync(currentUserId, patient, request, 0, cancellationToken);
    }

    // Lễ tân/Admin đặt lịch cho hồ sơ đã có, dùng source Phone hoặc WalkIn.
    public async Task<HTTPResponseData<AppointmentResponse?>> BookAppointmentForPatientAsync(
        int currentUserId, StaffBookAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Source is < 1 or > 2)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.StaffBookingSourceInvalid, 400);
        var user = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken);
        if (user is null || !user.IsActive || !user.UserRoles.Any(r =>
                r.Role.Name.Equals("Receptionist", StringComparison.OrdinalIgnoreCase) ||
                r.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        var patient = await _unitOfWork.PatientRepository.GetByIdAsync(request.PatientId, cancellationToken);
        if (patient is null) return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientNotFound, 404);
        if (!patient.IsActive) return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PatientInactive, 400);

        return await BookAppointmentForPatientCoreAsync(currentUserId, patient, new BookAppointmentRequest
        {
            DoctorId = request.DoctorId, StartTime = request.StartTime,
            ServiceId = request.ServiceId, Reason = request.Reason
        }, request.Source, cancellationToken);
    }

    private async Task<HTTPResponseData<AppointmentResponse?>> BookAppointmentForPatientCoreAsync(
        int currentUserId, FoMed.Infrastructure.Models.Patient patient, BookAppointmentRequest request,
        byte source, CancellationToken cancellationToken)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);

        var doctor = await _unitOfWork.DoctorRepository.GetByIdAsync(request.DoctorId, cancellationToken);
        if (doctor == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorNotFound, 404);
        if (!doctor.IsActive)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorInactive, 400);

        var selectedService = request.ServiceId.HasValue
            ? await _unitOfWork.ServiceRepository.GetActiveByIdAsync(request.ServiceId.Value, cancellationToken)
            : null;
        if (request.ServiceId.HasValue && selectedService is null)
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

        var code = await _unitOfWork.AppointmentRepository.GenerateCodeAsync(cancellationToken);
        var appointment = new FoMed.Infrastructure.Models.Appointment
        {
            AppointmentCode = code,
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ServiceId = request.ServiceId,
            FeeSnapshot = selectedService?.Price ?? doctor.ConsultationFee,
            StartTime = request.StartTime,
            EndTime = endTime,
            Status = (byte)AppointmentStatus.Pending,
            QueueNumber = null,
            Source = source,
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
            Reason = request.Reason ?? (source == 0 ? "Dat lich qua he thong online" : "Le tan tao lich hen"),
            ChangedAt = DateTime.UtcNow
        };
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(history, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        var created = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointment.Id, cancellationToken);
        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(created!),
            Message = source == 0 ? AppointmentResponseMessageDTO.BookSuccess : AppointmentResponseMessageDTO.StaffBookingSuccess,
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
    public async Task<HTTPResponseData<IReadOnlyList<AppointmentResponse>>> GetStaffAppointmentsAsync(
        int currentUserId, DateOnly? date = null, AppointmentStatus? status = null, int? doctorId = null, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken);
        if (user is null || !user.IsActive)
            return Fail<IReadOnlyList<AppointmentResponse>>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        var roleNames = user.UserRoles.Select(role => role.Role.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (roleNames.Contains("Admin") || roleNames.Contains("Receptionist"))
        {
            var allAppointments = await _unitOfWork.AppointmentRepository.GetStaffAppointmentsAsync(date, status, doctorId, cancellationToken);
            return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
            {
                DataResponse = allAppointments.Select(MapToResponse).ToList(),
                Message = AppointmentResponseMessageDTO.GetListSuccess,
                StatusCode = 200
            };
        }

        if (roleNames.Contains("Doctor"))
        {
            var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
            if (doctor is { IsActive: true })
            {
                if (doctorId.HasValue && doctorId.Value != doctor.Id)
                    return Fail<IReadOnlyList<AppointmentResponse>>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
                var appointments = await _unitOfWork.AppointmentRepository.GetDoctorAppointmentsAsync(doctor.Id, date, status, cancellationToken);
                return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
                {
                    DataResponse = appointments.Select(MapToResponse).ToList(),
                    Message = AppointmentResponseMessageDTO.GetListSuccess,
                    StatusCode = 200
                };
            }
        }

        return Fail<IReadOnlyList<AppointmentResponse>>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
    }

    // Lễ tân đánh dấu không đến sau khi thời gian bắt đầu đã qua và bệnh nhân chưa check-in.
    public async Task<HTTPResponseData<AppointmentResponse?>> MarkNoShowAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanCheckInAsync(currentUserId, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.Confirmed)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.NoShowStatusNotAllowed, 409);
        if (appointment.CheckedInAt.HasValue)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.NoShowAlreadyCheckedIn, 409);
        if (ClinicTime.Now < appointment.StartTime)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.NoShowTooEarly, 409);

        var nowUtc = DateTime.UtcNow;
        appointment.Status = (byte)AppointmentStatus.NoShow;
        appointment.UpdatedAt = nowUtc;
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = (byte)AppointmentStatus.Confirmed,
            ToStatus = (byte)AppointmentStatus.NoShow,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Bệnh nhân không đến khám",
            ChangedAt = nowUtc
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);
        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.NoShowSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<IReadOnlyList<AppointmentResponse>>> GetWaitingQueueAsync(
        int currentUserId, DateOnly? date = null, int? doctorId = null, CancellationToken cancellationToken = default)
    {
        var appointments = await GetStaffAppointmentsAsync(
            currentUserId, date ?? DateOnly.FromDateTime(ClinicTime.Now), AppointmentStatus.Confirmed, doctorId, cancellationToken);
        if (appointments.StatusCode != 200 || appointments.DataResponse is null)
            return appointments;

        var queue = appointments.DataResponse
            .Where(appointment => appointment.CheckedInAt.HasValue)
            .OrderBy(appointment => appointment.QueueNumber)
            .ThenBy(appointment => appointment.CheckedInAt)
            .ToList();
        return new HTTPResponseData<IReadOnlyList<AppointmentResponse>>
        {
            DataResponse = queue,
            Message = AppointmentResponseMessageDTO.GetWaitingQueueSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<IReadOnlyList<DoctorQueuePatientResponse>>> GetDoctorQueueAsync(
        int currentUserId, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (doctor is not { IsActive: true })
            return Fail<IReadOnlyList<DoctorQueuePatientResponse>>(AppointmentResponseMessageDTO.DoctorNotFound, 404);

        var appointments = await _unitOfWork.AppointmentRepository.GetDoctorQueueAsync(
            doctor.Id, date ?? DateOnly.FromDateTime(ClinicTime.Now), cancellationToken);
        var response = appointments.Select(appointment => new DoctorQueuePatientResponse(
            MapToResponse(appointment),
            appointment.Patient.Allergies,
            appointment.Patient.MedicalRecords
                .Where(record => record.AppointmentId != appointment.Id)
                .OrderByDescending(record => record.CreatedAt)
                .Take(5)
                .Select(record => new PatientHistorySummary(
                    record.Id, record.AppointmentId, record.CreatedAt, record.Diagnosis, record.Note))
                .ToList())).ToList();
        var historyIds = response.SelectMany(item => item.RecentHistory).Select(record => record.MedicalRecordId).Distinct().ToArray();
        if (historyIds.Length > 0)
        {
            var actor = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken)
                ?? throw new ClinicException(401, "Không tìm thấy người thực hiện.");
            foreach (var recordId in historyIds)
                await _unitOfWork.AppointmentRepository.AddAuditLogAsync(
                    MedicalRecordAudit.Create(currentUserId, actor, "Read", recordId, "DoctorQueueHistory", auditContext), cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return new HTTPResponseData<IReadOnlyList<DoctorQueuePatientResponse>>
        {
            DataResponse = response,
            Message = AppointmentResponseMessageDTO.GetWaitingQueueSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<AppointmentResponse?>> CallNextAsync(
        int currentUserId, DateOnly? date = null, int? doctorId = null,
        QueueActionRequest? request = null, CancellationToken cancellationToken = default)
    {
        var waiting = await GetWaitingQueueAsync(currentUserId, date, doctorId, cancellationToken);
        var next = waiting.DataResponse?.FirstOrDefault();
        if (next is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.QueueEmpty, 404);

        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(next.Id, cancellationToken);
        if (appointment is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanChangeStatusAsync(currentUserId, appointment.DoctorId, true, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.Confirmed || !appointment.CheckedInAt.HasValue)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.QueueActionRequiresCheckedIn, 409);

        var nowUtc = DateTime.UtcNow;
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ChangedBy = currentUserId,
            Reason = request?.Reason ?? "Goi benh nhan tiep theo",
            ChangedAt = nowUtc
        }, cancellationToken);
        appointment.UpdatedAt = nowUtc;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);
        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.CallNextSuccess,
            StatusCode = 200
        };
    }

    public async Task<HTTPResponseData<AppointmentResponse?>> MoveToEndAsync(
        int currentUserId, int appointmentId, QueueActionRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanChangeStatusAsync(currentUserId, appointment.DoctorId, true, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.Confirmed || !appointment.CheckedInAt.HasValue)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.QueueActionRequiresCheckedIn, 409);

        appointment.QueueNumber = await _unitOfWork.AppointmentRepository.GetNextQueueNumberAsync(
            appointment.DoctorId, DateOnly.FromDateTime(appointment.StartTime), cancellationToken);
        var nowUtc = DateTime.UtcNow;
        appointment.UpdatedAt = nowUtc;
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ChangedBy = currentUserId,
            Reason = request?.Reason ?? "Chuyen benh nhan xuong cuoi hang cho",
            ChangedAt = nowUtc
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);
        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.MoveToEndSuccess,
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

        if (!await CanAccessAppointmentAsync(currentUserId, appointment, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.GetAppointmentSuccess,
            StatusCode = 200
        };
    }

    // Bệnh nhân chỉ được đổi lịch của chính mình trước giờ khám tối thiểu 24 giờ.
    public async Task<HTTPResponseData<AppointmentResponse?>> RescheduleAppointmentAsync(
        int currentUserId, int appointmentId, RescheduleAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);

        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (patient?.Id != appointment.PatientId)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        if (appointment.Status is not ((byte)AppointmentStatus.Pending or (byte)AppointmentStatus.Confirmed))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.RescheduleStatusNotAllowed, 409);

        var changeDeadline = ClinicTime.Now.Add(PatientChangeCutoff);
        if (appointment.StartTime <= changeDeadline)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CancellationTooLate, 409);

        var newStart = ClinicTime.Normalize(request.StartTime);
        if (newStart <= ClinicTime.Now)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.PastTimeNotAllowed, 400);
        if (newStart <= changeDeadline)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CancellationTooLate, 409);

        var schedules = await _unitOfWork.DoctorScheduleRepository.GetByDoctorIdAsync(appointment.DoctorId, cancellationToken);
        var schedule = schedules.FirstOrDefault(s =>
            s.DayOfWeek == (byte)newStart.DayOfWeek && s.IsActive &&
            ClinicTime.IsSlot(newStart, s.StartTime, s.EndTime, s.SlotMinutes));
        if (schedule is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.DoctorNoSchedule, 400);

        var slotMinutes = schedule.SlotMinutes > 0 ? schedule.SlotMinutes : 30;
        var newEnd = newStart.AddMinutes(slotMinutes);
        if (await _unitOfWork.AppointmentRepository.IsDoctorOnTimeOffAsync(
                appointment.DoctorId, newStart, newEnd, cancellationToken) ||
            await _unitOfWork.AppointmentRepository.HasDoctorConflictAsync(
                appointment.DoctorId, newStart, newEnd, appointment.Id, cancellationToken) ||
            await _unitOfWork.AppointmentRepository.HasPatientConflictAsync(
                appointment.PatientId, newStart, newEnd, appointment.Id, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.RescheduleTimeConflict, 409);

        var oldStart = appointment.StartTime;
        appointment.StartTime = newStart;
        appointment.EndTime = newEnd;
        appointment.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? $"Doi lich tu {oldStart:yyyy-MM-dd HH:mm} sang {newStart:yyyy-MM-dd HH:mm}",
            ChangedAt = DateTime.UtcNow
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.RescheduleSuccess,
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

    public async Task<HTTPResponseData<AppointmentResponse?>> CheckInAppointmentAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanCheckInAsync(currentUserId, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.CheckedInAt.HasValue)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AlreadyCheckedIn, 409);
        if (appointment.Status != (byte)AppointmentStatus.Confirmed)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CheckInRequiresConfirmed, 409);
        if (DateOnly.FromDateTime(appointment.StartTime) != DateOnly.FromDateTime(ClinicTime.Now))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CheckInDateMismatch, 409);

        var nowUtc = DateTime.UtcNow;
        appointment.QueueNumber = await _unitOfWork.AppointmentRepository.GetNextQueueNumberAsync(
            appointment.DoctorId, DateOnly.FromDateTime(appointment.StartTime), cancellationToken);
        appointment.CheckedInAt = nowUtc;
        appointment.UpdatedAt = nowUtc;
        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = appointment.Status,
            ToStatus = appointment.Status,
            ChangedBy = currentUserId,
            Reason = request.Reason ?? "Bệnh nhân check-in tại quầy",
            ChangedAt = nowUtc
        }, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<AppointmentResponse?>
        {
            DataResponse = MapToResponse(appointment),
            Message = AppointmentResponseMessageDTO.CheckInSuccess,
            StatusCode = 200
        };
    }

    // 7. Hoan tat ca kham sau khi benh an du dieu kien
    public async Task<HTTPResponseData<AppointmentResponse?>> CompleteAppointmentAsync(
        int currentUserId, int appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);
        if (!await CanChangeStatusAsync(currentUserId, appointment.DoctorId, false, cancellationToken))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);
        if (appointment.Status != (byte)AppointmentStatus.InProgress)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CompletionRequiresInProgress, 409);

        var medicalRecord = await _unitOfWork.AppointmentRepository
            .GetMedicalRecordForCompletionAsync(appointmentId, cancellationToken);
        if (medicalRecord is null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CompletionRequiresMedicalRecord, 409);
        if (string.IsNullOrWhiteSpace(medicalRecord.Diagnosis))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CompletionRequiresDiagnosis, 409);
        if (medicalRecord.MedicalRecordServices.Any(order => order.Status == 0))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CompletionHasPendingOrders, 409);

        var old = appointment.Status;
        appointment.Status = (byte)AppointmentStatus.Completed;
        appointment.UpdatedAt = DateTime.UtcNow;
        medicalRecord.IsFinalized = true;
        medicalRecord.FinalizedAt = DateTime.UtcNow;

        var auditActor = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken)
            ?? throw new ClinicException(401, "Không xác định được người thực hiện.");
        await _unitOfWork.AppointmentRepository.AddAuditLogAsync(MedicalRecordAudit.Create(currentUserId, auditActor,
            "Finalize", medicalRecord.Id, "FinalizeRecord", auditContext, ["IsFinalized", "FinalizedAt"]), cancellationToken);

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
        int currentUserId, int appointmentId, CancelAppointmentRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Reason))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CancelReasonRequired, 400);

        await using var write = await _unitOfWork.BeginWriteAsync(cancellationToken);
        var appointment = await _unitOfWork.AppointmentRepository.GetByIdWithDetailsAsync(appointmentId, cancellationToken);
        if (appointment == null)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.AppointmentNotFound, 404);

        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        var patientOwnsAppointment = patient?.Id == appointment.PatientId;
        var staffCanCancel = await CanChangeStatusAsync(currentUserId, appointment.DoctorId, true, cancellationToken);
        if (!patientOwnsAppointment && !staffCanCancel)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.UnauthorizedAccess, 403);

        if (appointment.Status == (byte)AppointmentStatus.InProgress)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CancelInProgressNotAllowed, 409);
        if (appointment.Status == (byte)AppointmentStatus.Completed ||
            appointment.Status == (byte)AppointmentStatus.Cancelled ||
            appointment.Status == (byte)AppointmentStatus.NoShow)
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.InvalidStatusTransition, 400);

        if (patientOwnsAppointment && !staffCanCancel &&
            appointment.StartTime <= ClinicTime.Now.Add(PatientChangeCutoff))
            return Fail<AppointmentResponse?>(AppointmentResponseMessageDTO.CancellationTooLate, 409);

        var old = appointment.Status;
        appointment.Status = (byte)AppointmentStatus.Cancelled;
        appointment.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.AppointmentRepository.AddStatusHistoryAsync(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = old,
            ToStatus = (byte)AppointmentStatus.Cancelled,
            ChangedBy = currentUserId,
            Reason = request.Reason,
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

        if (!await CanAccessAppointmentAsync(currentUserId, appointment, cancellationToken))
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

        if (allowReceptionist && user.UserRoles.Any(role =>
                role.Role.Name.Equals("Receptionist", StringComparison.OrdinalIgnoreCase) ||
                role.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            return true;

        if (!user.UserRoles.Any(role => role.Role.Name == "Doctor"))
            return false;

        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        return doctor is { IsActive: true } && doctor.Id == doctorId;
    }

    private async Task<bool> CanCheckInAsync(int currentUserId, CancellationToken cancellationToken)
    {
        var user = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken);
        return user is { IsActive: true } && user.UserRoles.Any(role =>
            role.Role.Name.Equals("Receptionist", StringComparison.OrdinalIgnoreCase) ||
            role.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> CanAccessAppointmentAsync(
        int currentUserId, FoMed.Infrastructure.Models.Appointment appointment, CancellationToken cancellationToken)
    {
        var patient = await _unitOfWork.PatientRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        if (patient?.Id == appointment.PatientId)
            return true;

        var user = await _unitOfWork.UserRepository.GetByIdWithRolesAsync(currentUserId, cancellationToken);
        if (user is null || !user.IsActive)
            return false;

        if (user.UserRoles.Any(role =>
                role.Role.Name.Equals("Receptionist", StringComparison.OrdinalIgnoreCase) ||
                role.Role.Name.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
            return true;

        var doctor = await _unitOfWork.DoctorRepository.GetByUserIdAsync(currentUserId, cancellationToken);
        return doctor is { IsActive: true } && doctor.Id == appointment.DoctorId;
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
            a.ServiceId, a.Service?.Name, a.CheckedInAt, a.Source, a.FeeSnapshot);
    }
}

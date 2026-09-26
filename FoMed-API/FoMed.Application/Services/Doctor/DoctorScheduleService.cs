using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.UnitOfWork;

namespace FoMed.Application.Services.Doctor;

public sealed class DoctorScheduleService(IUnitOfWork unitOfWork)
{
    // Lấy các khung giờ active của bác sĩ đang đăng nhập.
    public async Task<HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>> GetMySchedulesAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var doctor = await GetDoctorAsync(userId, cancellationToken);
        if (doctor is null)
        {
            return new HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>
            {
                DataResponse = [],
                Message = DoctorScheduleResponseMessageDTO.DoctorNotFound,
                StatusCode = 404
            };
        }

        var schedules = await unitOfWork.DoctorScheduleRepository
            .GetByDoctorIdAsync(doctor.Id, cancellationToken);

        return new HTTPResponseData<IReadOnlyList<DoctorScheduleResponse>>
        {
            DataResponse = schedules.Select(Map).ToList(),
            Message = DoctorScheduleResponseMessageDTO.GetSchedulesSuccess,
            StatusCode = 200
        };
    }

    // Tạo khung giờ và ngăn không cho lịch mới bị trùng với lịch hiện tại.
    public async Task<HTTPResponseData<DoctorScheduleResponse?>> CreateAsync(
        int userId,
        SaveDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        await using var write = await unitOfWork.BeginWriteAsync(cancellationToken);
        var doctor = await GetDoctorAsync(userId, cancellationToken);
        if (doctor is null)
        {
            return DoctorNotFoundResponse();
        }

        var validation = await ValidateTimeAsync(doctor.Id, request, null, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        var schedule = new DoctorSchedule
        {
            DoctorId = doctor.Id,
            DayOfWeek = request.DayOfWeek,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            SlotMinutes = request.SlotMinutes,
            IsActive = true
        };

        await unitOfWork.DoctorScheduleRepository.AddAsync(schedule, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<DoctorScheduleResponse?>
        {
            DataResponse = Map(schedule),
            Message = DoctorScheduleResponseMessageDTO.CreateScheduleSuccess,
            StatusCode = 201
        };
    }

    // Cập nhật một khung giờ thuộc về bác sĩ đang đăng nhập.
    public async Task<HTTPResponseData<DoctorScheduleResponse?>> UpdateAsync(
        int userId,
        int scheduleId,
        SaveDoctorScheduleRequest request,
        CancellationToken cancellationToken)
    {
        await using var write = await unitOfWork.BeginWriteAsync(cancellationToken);
        var doctor = await GetDoctorAsync(userId, cancellationToken);
        if (doctor is null)
        {
            return DoctorNotFoundResponse();
        }

        var schedule = await unitOfWork.DoctorScheduleRepository
            .GetByIdAndDoctorIdAsync(scheduleId, doctor.Id, cancellationToken);

        if (schedule is null)
        {
            return ScheduleNotFoundResponse();
        }

        var validation = await ValidateTimeAsync(doctor.Id, request, scheduleId, cancellationToken);
        if (validation is not null)
        {
            return validation;
        }

        schedule.DayOfWeek = request.DayOfWeek;
        schedule.StartTime = request.StartTime;
        schedule.EndTime = request.EndTime;
        schedule.SlotMinutes = request.SlotMinutes;
        schedule.IsActive = true;

        unitOfWork.DoctorScheduleRepository.Update(schedule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<DoctorScheduleResponse?>
        {
            DataResponse = Map(schedule),
            Message = DoctorScheduleResponseMessageDTO.UpdateScheduleSuccess,
            StatusCode = 200
        };
    }

    // Xóa mềm khung giờ để không làm mất lịch sử cấu hình.
    public async Task<HTTPResponseData<DoctorScheduleResponse?>> DeleteAsync(
        int userId,
        int scheduleId,
        CancellationToken cancellationToken)
    {
        await using var write = await unitOfWork.BeginWriteAsync(cancellationToken);
        var doctor = await GetDoctorAsync(userId, cancellationToken);
        if (doctor is null)
        {
            return DoctorNotFoundResponse();
        }

        var schedule = await unitOfWork.DoctorScheduleRepository
            .GetByIdAndDoctorIdAsync(scheduleId, doctor.Id, cancellationToken);

        if (schedule is null || !schedule.IsActive)
        {
            return ScheduleNotFoundResponse();
        }

        schedule.IsActive = false;
        unitOfWork.DoctorScheduleRepository.Update(schedule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await write.CommitAsync(cancellationToken);

        return new HTTPResponseData<DoctorScheduleResponse?>
        {
            DataResponse = null,
            Message = DoctorScheduleResponseMessageDTO.DeleteScheduleSuccess,
            StatusCode = 200
        };
    }

    private async Task<Infrastructure.Models.Doctor?> GetDoctorAsync(
        int userId,
        CancellationToken cancellationToken) =>
        await unitOfWork.DoctorRepository.GetByUserIdAsync(userId, cancellationToken) is { IsActive: true } doctor
            ? doctor
            : null;

    private async Task<HTTPResponseData<DoctorScheduleResponse?>?> ValidateTimeAsync(
        int doctorId,
        SaveDoctorScheduleRequest request,
        int? excludedScheduleId,
        CancellationToken cancellationToken)
    {
        if (request.EndTime <= request.StartTime)
        {
            return new HTTPResponseData<DoctorScheduleResponse?>
            {
                DataResponse = null,
                Message = DoctorScheduleResponseMessageDTO.InvalidTimeRange,
                StatusCode = 400
            };
        }

        var hasOverlap = await unitOfWork.DoctorScheduleRepository.HasOverlapAsync(
            doctorId,
            request.DayOfWeek,
            request.StartTime,
            request.EndTime,
            excludedScheduleId,
            cancellationToken);

        return hasOverlap
            ? new HTTPResponseData<DoctorScheduleResponse?>
            {
                DataResponse = null,
                Message = DoctorScheduleResponseMessageDTO.ScheduleOverlap,
                StatusCode = 409
            }
            : null;
    }

    private static HTTPResponseData<DoctorScheduleResponse?> DoctorNotFoundResponse() => new()
    {
        DataResponse = null,
        Message = DoctorScheduleResponseMessageDTO.DoctorNotFound,
        StatusCode = 404
    };

    private static HTTPResponseData<DoctorScheduleResponse?> ScheduleNotFoundResponse() => new()
    {
        DataResponse = null,
        Message = DoctorScheduleResponseMessageDTO.ScheduleNotFound,
        StatusCode = 404
    };

    private static DoctorScheduleResponse Map(DoctorSchedule schedule) => new(
        schedule.Id,
        schedule.DoctorId,
        schedule.DayOfWeek,
        schedule.StartTime,
        schedule.EndTime,
        schedule.SlotMinutes,
        schedule.IsActive);
}

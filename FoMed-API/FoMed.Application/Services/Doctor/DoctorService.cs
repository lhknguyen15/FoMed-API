using FoMed.Application.DTO;
using FoMed.Application.DTO.Doctor;
using FoMed.Infrastructure.UnitOfWork;
using DoctorEntity = FoMed.Infrastructure.Models.Doctor;

namespace FoMed.Application.Services.Doctor;

public sealed class DoctorService(IUnitOfWork unitOfWork)
{
    // Lấy hồ sơ bác sĩ theo userId trong JWT.
    public async Task<HTTPResponseData<DoctorResponse?>> GetMyProfileAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var doctor = await unitOfWork.DoctorRepository
            .GetByUserIdAsync(userId, cancellationToken);

        if (doctor is null || !doctor.IsActive)
        {
            return NotFoundResponse();
        }

        return new HTTPResponseData<DoctorResponse?>
        {
            DataResponse = Map(doctor),
            Message = DoctorResponseMessageDTO.GetDoctorSuccess,
            StatusCode = 200
        };
    }

    // Cập nhật hồ sơ bác sĩ của chính tài khoản đang đăng nhập.
    public async Task<HTTPResponseData<DoctorResponse?>> UpdateMyProfileAsync(
        int userId,
        UpdateDoctorProfileRequest request,
        CancellationToken cancellationToken)
    {
        var doctor = await unitOfWork.DoctorRepository
            .GetByUserIdAsync(userId, cancellationToken);

        if (doctor is null || !doctor.IsActive)
        {
            return NotFoundResponse();
        }

        var specialty = await unitOfWork.SpecialtyRepository
            .GetActiveByIdAsync(request.SpecialtyId, cancellationToken);

        if (specialty is null)
        {
            return new HTTPResponseData<DoctorResponse?>
            {
                DataResponse = null,
                Message = DoctorResponseMessageDTO.SpecialtyNotFound,
                StatusCode = 404
            };
        }

        doctor.FullName = request.FullName.Trim();
        doctor.SpecialtyId = request.SpecialtyId;
        doctor.Specialty = specialty;
        doctor.Title = Normalize(request.Title);
        doctor.LicenseNumber = Normalize(request.LicenseNumber);
        doctor.Phone = Normalize(request.Phone);
        doctor.Room = Normalize(request.Room);
        doctor.ConsultationFee = request.ConsultationFee;

        unitOfWork.DoctorRepository.Update(doctor);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new HTTPResponseData<DoctorResponse?>
        {
            DataResponse = Map(doctor),
            Message = DoctorResponseMessageDTO.UpdateDoctorSuccess,
            StatusCode = 200
        };
    }

    // Lấy danh sách bác sĩ active để bệnh nhân chọn khi đặt lịch.
    public async Task<HTTPResponseData<IReadOnlyList<DoctorResponse>>> GetDoctorsAsync(
        int? specialtyId,
        string? search,
        CancellationToken cancellationToken)
    {
        var doctors = await unitOfWork.DoctorRepository
            .GetActiveAsync(specialtyId, search, cancellationToken);

        return new HTTPResponseData<IReadOnlyList<DoctorResponse>>
        {
            DataResponse = doctors.Select(Map).ToList(),
            Message = DoctorResponseMessageDTO.GetDoctorsSuccess,
            StatusCode = 200
        };
    }

    private static HTTPResponseData<DoctorResponse?> NotFoundResponse() => new()
    {
        DataResponse = null,
        Message = DoctorResponseMessageDTO.DoctorNotFound,
        StatusCode = 404
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Chuyển entity Doctor sang DTO và trả kèm tên chuyên khoa.
    private static DoctorResponse Map(DoctorEntity doctor) => new(
        doctor.Id,
        doctor.UserId,
        doctor.SpecialtyId,
        doctor.Specialty.Name,
        doctor.FullName,
        doctor.Title,
        doctor.LicenseNumber,
        doctor.Phone,
        doctor.Room,
        doctor.ConsultationFee,
        doctor.IsActive);
}

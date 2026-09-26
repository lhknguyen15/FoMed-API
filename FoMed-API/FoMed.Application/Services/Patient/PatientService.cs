using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Infrastructure.UnitOfWork;
using PatientEntity = FoMed.Infrastructure.Models.Patient;

namespace FoMed.Application.Services.Patient;

public sealed class PatientService(IUnitOfWork unitOfWork)
{
    // Lấy hồ sơ bệnh nhân theo userId trong JWT.
    public async Task<HTTPResponseData<PatientResponse?>> GetMyProfileAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var patient = await unitOfWork.PatientRepository
            .GetByUserIdAsync(userId, cancellationToken);

        if (patient is null || !patient.IsActive)
        {
            return NotFoundResponse();
        }

        return new HTTPResponseData<PatientResponse?>
        {
            DataResponse = Map(patient),
            Message = PatientResponseMessageDTO.GetPatientSuccess,
            StatusCode = 200
        };
    }

    // Cập nhật thông tin hồ sơ bệnh nhân của chính tài khoản đang đăng nhập.
    public async Task<HTTPResponseData<PatientResponse?>> UpdateMyProfileAsync(
        int userId,
        UpdatePatientProfileRequest request,
        CancellationToken cancellationToken)
    {
        var patient = await unitOfWork.PatientRepository
            .GetByUserIdAsync(userId, cancellationToken);

        if (patient is null || !patient.IsActive)
        {
            return NotFoundResponse();
        }

        patient.FullName = request.FullName.Trim();
        patient.Gender = request.Gender;
        patient.DateOfBirth = request.DateOfBirth;
        patient.Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        patient.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();

        unitOfWork.PatientRepository.Update(patient);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new HTTPResponseData<PatientResponse?>
        {
            DataResponse = Map(patient),
            Message = PatientResponseMessageDTO.UpdatePatientSuccess,
            StatusCode = 200
        };
    }

    private static HTTPResponseData<PatientResponse?> NotFoundResponse() => new()
    {
        DataResponse = null,
        Message = PatientResponseMessageDTO.PatientNotFound,
        StatusCode = 404
    };

    // Chuyển entity Patient sang DTO để không trả trực tiếp entity EF ra ngoài API.
    private static PatientResponse Map(PatientEntity patient) => new(
        patient.Id,
        patient.UserId,
        patient.PatientCode,
        patient.FullName,
        patient.Gender,
        patient.DateOfBirth,
        patient.Phone,
        patient.Address,
        patient.IsActive);
}

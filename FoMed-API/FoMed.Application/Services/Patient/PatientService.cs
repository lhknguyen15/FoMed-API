using FoMed.Application.DTO;
using FoMed.Application.DTO.Patient;
using FoMed.Infrastructure.UnitOfWork;
using PatientEntity = FoMed.Infrastructure.Models.Patient;

namespace FoMed.Application.Services.Patient;

public sealed class PatientService(IUnitOfWork unitOfWork)
{
    public async Task<HTTPResponseData<PatientDto?>> LookupByPhoneAsync(
        string phone, CancellationToken cancellationToken)
    {
        var normalizedPhone = NormalizePhone(phone);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
        {
            return new HTTPResponseData<PatientDto?>
            {
                DataResponse = null,
                Message = "Số điện thoại không hợp lệ.",
                StatusCode = 400
            };
        }

        var matches = await unitOfWork.PatientRepository.FindByPhoneAsync(normalizedPhone, cancellationToken);
        if (matches.Count > 1)
        {
            return new HTTPResponseData<PatientDto?>
            {
                DataResponse = null,
                Message = PatientResponseMessageDTO.PatientLookupAmbiguous,
                StatusCode = 409
            };
        }

        var patient = matches.SingleOrDefault();
        return new HTTPResponseData<PatientDto?>
        {
            DataResponse = patient is null ? null : new PatientDto(
                patient.Id, patient.FullName, patient.DateOfBirth, patient.UserId.HasValue),
            Message = PatientResponseMessageDTO.PatientLookupSuccess,
            StatusCode = 200
        };
    }

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
        patient.NationalId = Normalize(request.NationalId);
        patient.InsuranceNumber = Normalize(request.InsuranceNumber);
        patient.EmergencyContactName = Normalize(request.EmergencyContactName);
        patient.EmergencyContactPhone = NormalizePhoneOptional(request.EmergencyContactPhone);
        patient.Allergies = Normalize(request.Allergies);

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
        patient.NationalId,
        patient.InsuranceNumber,
        patient.EmergencyContactName,
        patient.EmergencyContactPhone,
        patient.Allergies,
        patient.IsActive);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizePhoneOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : NormalizePhone(value);

    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length == 11)
            return "0" + digits[2..];
        return digits;
    }
}

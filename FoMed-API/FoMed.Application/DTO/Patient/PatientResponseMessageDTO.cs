namespace FoMed.Application.DTO.Patient;

public static class PatientResponseMessageDTO
{
    public const string PatientNotFound = "Không tìm thấy hồ sơ bệnh nhân.";
    public const string GetPatientSuccess = "Lấy hồ sơ bệnh nhân thành công.";
    public const string UpdatePatientSuccess = "Cập nhật hồ sơ bệnh nhân thành công.";
    public const string PatientLookupSuccess = "Tra cứu hồ sơ bệnh nhân thành công.";
    public const string PatientLookupAmbiguous = "Có nhiều hồ sơ cùng số điện thoại. Cần nhân viên xác minh thêm.";
    public const string StaffOnly = "Chỉ lễ tân hoặc quản trị viên được thao tác hồ sơ tại quầy.";
    public const string PatientPhoneRequired = "Số điện thoại bệnh nhân là bắt buộc.";
    public const string PatientAlreadyExists = "Đã tồn tại hồ sơ bệnh nhân với số điện thoại này.";
    public const string PatientHistorySuccess = "Lấy lịch sử bệnh nhân thành công.";
    public const string PatientCreatedSuccess = "Tạo hồ sơ bệnh nhân thành công.";
}

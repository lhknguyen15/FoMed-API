namespace FoMed.Application.DTO.Doctor;

public static class DoctorScheduleResponseMessageDTO
{
    public const string DoctorNotFound = "Không tìm thấy hồ sơ bác sĩ.";
    public const string ScheduleNotFound = "Không tìm thấy lịch làm việc.";
    public const string InvalidTimeRange = "Thời gian kết thúc phải lớn hơn thời gian bắt đầu.";
    public const string ScheduleOverlap = "Khung giờ làm việc bị trùng với lịch hiện có.";
    public const string GetSchedulesSuccess = "Lấy lịch làm việc thành công.";
    public const string CreateScheduleSuccess = "Tạo lịch làm việc thành công.";
    public const string UpdateScheduleSuccess = "Cập nhật lịch làm việc thành công.";
    public const string DeleteScheduleSuccess = "Xóa lịch làm việc thành công.";
}

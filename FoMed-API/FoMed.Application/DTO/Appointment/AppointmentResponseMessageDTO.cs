namespace FoMed.Application.DTO.Appointment;

public static class AppointmentResponseMessageDTO
{
    public const string PatientNotFound = "Khong tim thay ho so benh nhan. Vui long cap nhat thong tin ca nhan.";
    public const string PatientInactive = "Tai khoan benh nhan dang bi khoa hoac khong hoat dong.";
    public const string DoctorNotFound = "Khong tim thay thong tin bac si.";
    public const string DoctorInactive = "Bac si hien khong hoat dong.";
    public const string PastTimeNotAllowed = "Thoi gian dat lich khong duoc o trong qua khu.";
    public const string DoctorNoSchedule = "Bac si khong co lich lam viec trong khung gio nay.";
    public const string DoctorTimeOffOverlap = "Bac si da dang ky nghi trong thoi gian nay.";
    public const string DoctorConflict = "Bac si da co lich hen khac trong khung gio nay.";
    public const string PatientConflict = "Ban da co lich hen khac trung vao khung gio nay.";
    public const string AppointmentNotFound = "Khong tim thay lich hen.";
    public const string UnauthorizedAccess = "Ban khong co quyen thao tac tren lich hen nay.";
    public const string InvalidStatusTransition = "Trang thai lich hen khong hop le de thuc hien thao tac nay.";

    public const string BookSuccess = "Dat lich kham thanh cong.";
    public const string ConfirmSuccess = "Xac nhan lich kham thanh cong.";
    public const string CompleteSuccess = "Hoan thanh ca kham thanh cong.";
    public const string CancelSuccess = "Huy lich kham thanh cong.";
    public const string GetAvailableSlotsSuccess = "Lay danh sach khung gio trong thanh cong.";
    public const string GetAppointmentSuccess = "Lay thong tin lich hen thanh cong.";
    public const string GetListSuccess = "Lay danh sach lich hen thanh cong.";
}
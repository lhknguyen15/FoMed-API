namespace FoMed.Application.DTO.Appointment;

public static class AppointmentResponseMessageDTO
{
    public const string PatientNotFound = "Khong tim thay ho so benh nhan. Vui long cap nhat thong tin ca nhan.";
    public const string PatientInactive = "Tai khoan benh nhan dang bi khoa hoac khong hoat dong.";
    public const string DoctorNotFound = "Khong tim thay thong tin bac si.";
    public const string ServiceNotFound = "Khong tim thay dich vu dang hoat dong.";
    public const string DoctorInactive = "Bac si hien khong hoat dong.";
    public const string PastTimeNotAllowed = "Thoi gian dat lich khong duoc o trong qua khu.";
    public const string DoctorNoSchedule = "Bac si khong co lich lam viec trong khung gio nay.";
    public const string DoctorTimeOffOverlap = "Bac si da dang ky nghi trong thoi gian nay.";
    public const string DoctorConflict = "Bac si da co lich hen khac trong khung gio nay.";
    public const string PatientConflict = "Ban da co lich hen khac trung vao khung gio nay.";
    public const string AppointmentNotFound = "Khong tim thay lich hen.";
    public const string UnauthorizedAccess = "Ban khong co quyen thao tac tren lich hen nay.";
    public const string InvalidStatusTransition = "Trang thai lich hen khong hop le de thuc hien thao tac nay.";
    public const string CancelReasonRequired = "Ly do huy lich la bat buoc.";
    public const string CancellationTooLate = "Khong the huy hoac doi lich qua gan gio kham theo chinh sach phong kham.";
    public const string CancelInProgressNotAllowed = "Khong the huy lich hen dang trong qua trinh kham.";
    public const string RescheduleStatusNotAllowed = "Chi co the doi lich hen dang cho hoac da xac nhan.";
    public const string RescheduleSuccess = "Doi lich kham thanh cong.";
    public const string RescheduleTimeConflict = "Khung gio moi khong con phu hop hoac da co lich khac.";
    public const string CompletionRequiresInProgress = "Chi co the hoan tat lich hen dang trong trang thai kham.";
    public const string CompletionRequiresMedicalRecord = "Can tao benh an truoc khi hoan tat lich hen.";
    public const string CompletionRequiresDiagnosis = "Can cap nhat chan doan truoc khi hoan tat lich hen.";
    public const string CompletionHasPendingOrders = "Can hoan tat hoac huy cac chi dinh dang cho truoc khi dong benh an.";
    public const string CheckInRequiresConfirmed = "Chi co the check-in lich hen da duoc xac nhan.";
    public const string CheckInDateMismatch = "Chi check-in lich hen trong ngay hen.";
    public const string AlreadyCheckedIn = "Lich hen da duoc check-in.";
    public const string NoShowStatusNotAllowed = "Chi co the danh dau khong den voi lich hen da xac nhan.";
    public const string NoShowAlreadyCheckedIn = "Benh nhan da check-in, khong the danh dau khong den.";
    public const string NoShowTooEarly = "Chi co the danh dau khong den sau gio bat dau lich hen.";

    public const string BookSuccess = "Dat lich kham thanh cong.";
    public const string ConfirmSuccess = "Xac nhan lich kham thanh cong.";
    public const string CheckInSuccess = "Check-in benh nhan thanh cong.";
    public const string NoShowSuccess = "Da danh dau benh nhan khong den kham.";
    public const string StaffBookingSuccess = "Tao lich hen cho benh nhan thanh cong.";
    public const string StaffBookingSourceInvalid = "Nguon dat lich tai quay khong hop le.";
    public const string GetWaitingQueueSuccess = "Lay hang cho kham thanh cong.";
    public const string QueueEmpty = "Hien khong co benh nhan dang cho kham.";
    public const string CallNextSuccess = "Goi benh nhan tiep theo thanh cong.";
    public const string MoveToEndSuccess = "Da chuyen benh nhan xuong cuoi hang cho.";
    public const string QueueActionRequiresCheckedIn = "Chi co the thao tac hang cho voi benh nhan da check-in.";
    public const string CompleteSuccess = "Hoan thanh ca kham thanh cong.";
    public const string CancelSuccess = "Huy lich kham thanh cong.";
    public const string GetAvailableSlotsSuccess = "Lay danh sach khung gio trong thanh cong.";
    public const string GetAppointmentSuccess = "Lay thong tin lich hen thanh cong.";
    public const string GetListSuccess = "Lay danh sach lich hen thanh cong.";
}

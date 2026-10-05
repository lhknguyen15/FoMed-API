# VC-12 — Hàng chờ bệnh nhân của bác sĩ

`GET /api/appointments/doctor-queue?date=YYYY-MM-DD` dành riêng cho role `Doctor`. API tự xác định bác sĩ từ token, vì vậy bác sĩ không thể truyền `doctorId` để xem hàng của người khác.

Mỗi phần tử trả về lịch hẹn, số thứ tự, thông tin dịch vụ, `allergies` và tối đa năm bệnh án gần nhất của bệnh nhân (`medicalRecordId`, thời điểm khám, chẩn đoán, ghi chú). Dữ liệu bệnh án chỉ được nạp trong hàng chờ của đúng bác sĩ phụ trách; bệnh nhân không nhận được response này.

Luồng bắt đầu khám vẫn dùng `POST /api/clinical/appointments/{appointmentId}/record`. Service yêu cầu lịch đã `Confirmed` và có `CheckedInAt`, sau đó chuyển lịch sang `InProgress`; thao tác tạo bệnh án trước check-in vẫn trả `409`.

Phần còn lại của VC-12 là mở rộng thông tin sinh hiệu/dị ứng có cấu trúc và quyền xem lịch sử theo phân công nếu nghiệp vụ yêu cầu thêm. Hiện dị ứng được trả từ trường hồ sơ `Patient.Allergies` đã có trong schema.

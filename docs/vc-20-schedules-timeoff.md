# VC-20 — Lịch làm việc và nghỉ

Bác sĩ tiếp tục dùng `api/doctor/schedules` để CRUD ca của mình. Admin quản lý lịch làm việc theo bác sĩ qua `GET/POST/PUT/DELETE /api/admin/schedules`; hỗ trợ lọc danh sách theo `doctorId`, kiểm tra khung giờ trùng và ghi audit log. Lịch nghỉ cá nhân dùng `GET/POST/PUT/DELETE /api/doctor/time-off`. Admin quản lý lịch nghỉ bác sĩ hoặc nghỉ toàn phòng khám qua `GET/POST/PUT/DELETE /api/admin/time-off`; request không có `doctorId` là nghỉ toàn phòng khám.

Admin có thể tạo nhiều ngày có cùng ca trong một lần gọi `POST /api/admin/schedules/batch` với `dayOfWeeks`; toàn bộ các ngày được kiểm tra xung đột và lưu trong cùng giao dịch.

API chuẩn hóa giờ theo `ClinicTime`, kiểm tra khoảng thời gian hợp lệ và từ chối tạo lịch nghỉ nếu đã có lịch hẹn bị ảnh hưởng.

Khi Admin tạo hoặc cập nhật lịch nghỉ theo bác sĩ, bác sĩ phải tồn tại và đang hoạt động. Mã bác sĩ không tồn tại (kể cả 0 hoặc số âm) hoặc đã ngừng hoạt động trả HTTP 404 với thông báo `Không tìm thấy bác sĩ đang hoạt động.`; không phát sinh bản ghi hoặc nhật ký thành công. `doctorId = null` hoặc không gửi trường này vẫn có nghĩa nghỉ toàn phòng khám và kiểm tra xung đột với lịch hẹn của mọi bác sĩ.

Lịch nghỉ cũ của bác sĩ đã ngừng hoạt động vẫn có thể được Admin tra cứu và xóa. Khi cập nhật, phải chọn bác sĩ đang hoạt động hoặc chuyển sang nghỉ toàn phòng khám; hệ thống không tự xóa lịch sử hoặc kích hoạt lại bác sĩ.

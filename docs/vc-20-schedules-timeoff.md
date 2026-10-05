# VC-20 — Lịch làm việc và nghỉ

Bác sĩ tiếp tục dùng `api/doctor/schedules` để CRUD ca của mình. Admin quản lý lịch làm việc theo bác sĩ qua `GET/POST/PUT/DELETE /api/admin/schedules`; hỗ trợ lọc danh sách theo `doctorId`, kiểm tra khung giờ trùng và ghi audit log. Lịch nghỉ cá nhân dùng `GET/POST/PUT/DELETE /api/doctor/time-off`. Admin quản lý lịch nghỉ bác sĩ hoặc nghỉ toàn phòng khám qua `GET/POST/PUT/DELETE /api/admin/time-off`; request không có `doctorId` là nghỉ toàn phòng khám.

Admin có thể tạo nhiều ngày có cùng ca trong một lần gọi `POST /api/admin/schedules/batch` với `dayOfWeeks`; toàn bộ các ngày được kiểm tra xung đột và lưu trong cùng giao dịch.

API chuẩn hóa giờ theo `ClinicTime`, kiểm tra khoảng thời gian hợp lệ và từ chối tạo lịch nghỉ nếu đã có lịch hẹn bị ảnh hưởng.

# Rà soát bề mặt FoMed-API

Ngày rà soát: 2026-10-01

## Đã loại bỏ

- `GET /api/doctor`: danh sách bác sĩ yêu cầu đăng nhập nhưng trả cùng nghiệp vụ với `GET /api/doctors`. Danh mục đặt lịch dùng route công khai và không còn cần một route thứ hai.
- `GET /api/appointments/doctor-appointments`: route cũ chỉ lấy lịch của bác sĩ đăng nhập. Nghiệp vụ này đã được gộp vào `GET /api/appointments/staff-appointments`; bác sĩ được tự giới hạn theo tài khoản, còn lễ tân/Admin có thể lọc theo `doctorId`.

Phương thức service riêng cho hai route trên cũng đã được xóa. Repository `GetDoctorAppointmentsAsync` vẫn được giữ vì `staff-appointments` dùng nó cho nhánh quyền Doctor.

## Không xóa

- `GET/PUT /api/doctor/me` phục vụ hồ sơ bác sĩ đăng nhập.
- `GET/PUT /api/patient/me` phục vụ hồ sơ bệnh nhân đăng nhập.
- `GET /api/clinical/medicines` và `GET /api/clinical/services` là danh mục được dùng trong kê đơn và chỉ định.
- Các DTO trong `FoMed.Application.DTO` đều còn được controller/service hoặc DTO lồng nhau sử dụng. Không phát hiện model/DTO mồ côi đủ chắc chắn để xóa trong đợt này.

## Quy tắc duy trì

API mới phải được gắn với một VC trong `docs/clinic-workflow.md` hoặc cập nhật hợp đồng trong `docs/api-contract-review.md`. Không tạo route thứ hai cho cùng một nghiệp vụ; nếu cần thay đổi tên route, cập nhật FE và test trong cùng một thay đổi.

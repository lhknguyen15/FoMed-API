# VC-03 — Đặt lịch

## Hợp đồng API hiện tại

- `GET /api/specialties`: danh sách chuyên khoa đang hoạt động, không yêu cầu đăng nhập.
- `GET /api/doctors?specialtyId=&search=`: danh sách bác sĩ đang hoạt động, không yêu cầu đăng nhập.
- `GET /api/services?page=1`: danh mục dịch vụ đang hoạt động, không yêu cầu đăng nhập.
- `GET /api/appointments/available-slots?doctorId=&date=&serviceId=`: khung giờ theo lịch bác sĩ. `serviceId` là tùy chọn nhưng nếu gửi phải là dịch vụ đang hoạt động.
- `POST /api/appointments/book`: bệnh nhân đã đăng nhập đặt lịch cho chính hồ sơ của mình.

Ví dụ request:

```json
{
  "doctorId": 8,
  "serviceId": 13,
  "startTime": "2030-01-07T09:00:00+07:00",
  "reason": "Khám định kỳ"
}
```

API lấy `patientId` từ JWT, không cho bệnh nhân tự gửi `patientId` để đặt thay người khác. Lịch được tạo với `source = 0` (đặt online), chưa cấp số thứ tự tại thời điểm đặt. Giá được chụp tại thời điểm đặt vào `feeSnapshot`: giá dịch vụ nếu có `serviceId`, nếu không thì dùng phí khám của bác sĩ.

## Kiểm tra nghiệp vụ

- Bác sĩ và dịch vụ phải đang hoạt động.
- Thời gian phải nằm trong ca làm việc, đúng lưới slot và không ở quá khứ.
- Từ chối lịch nghỉ của bác sĩ, lịch trùng bác sĩ và lịch trùng bệnh nhân.
- Mã lịch hẹn sinh từ sequence; thao tác ghi dùng transaction và khóa workflow.
- `QueueNumber` chỉ được cấp khi lễ tân check-in.

Migration cần chạy trên database hiện có:

```text
database/migrations/20260930_add_fee_snapshot_to_appointments.sql
```

Các phần nhắc lịch, BHYT/giảm giá, đặt hộ và trả danh sách slot thay thế khi xung đột thuộc các bước nghiệp vụ tiếp theo; chưa tự ý gộp vào VC-03 vì cần hợp đồng và dữ liệu riêng.

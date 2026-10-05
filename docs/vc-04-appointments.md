# VC-04 — Lịch hẹn của tôi

## Hợp đồng API

```http
GET  /api/appointments/my-appointments?date=&status=
GET  /api/appointments/{id}
GET  /api/appointments/{id}/history
PUT  /api/appointments/{id}/reschedule
PUT  /api/appointments/{id}/cancel
```

Các endpoint đọc chỉ trả lịch thuộc hồ sơ bệnh nhân trong JWT. Endpoint chi tiết và lịch sử có thể được đọc bởi bệnh nhân sở hữu lịch, bác sĩ phụ trách hoặc lễ tân/Admin theo quyền nghiệp vụ.

## Đổi lịch

Request dùng `RescheduleAppointmentRequest`:

```json
{
  "startTime": "2030-01-09T09:00:00+07:00",
  "reason": "Thay đổi kế hoạch cá nhân"
}
```

Bệnh nhân chỉ được đổi lịch `Pending` hoặc `Confirmed`, trước giờ khám tối thiểu 24 giờ. API kiểm tra lại ca làm, lưới slot, lịch nghỉ, trùng bác sĩ và trùng bệnh nhân; lịch hiện tại được loại khỏi kiểm tra trùng.

## Hủy lịch

Request dùng `CancelAppointmentRequest`, trong đó lý do là bắt buộc:

```json
{
  "reason": "Không thể đến đúng lịch"
}
```

Bệnh nhân chỉ được hủy lịch của mình trước thời hạn 24 giờ. Lễ tân/Admin hoặc bác sĩ phụ trách có thể xử lý lịch sát giờ theo quyền vận hành. Lịch `InProgress`, `Completed`, `Cancelled` và `NoShow` không được hủy.

Mỗi lần đổi hoặc hủy đều ghi vào `appointment_status_history`. Đổi lịch giữ nguyên trạng thái hiện tại và ghi thời gian cũ, thời gian mới trong lý do lịch sử.

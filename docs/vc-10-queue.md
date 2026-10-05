# VC-10 — Hàng chờ và cấp số

## API đã có

- `PUT /api/appointments/{id}/check-in`: lễ tân/Admin check-in lịch `Confirmed` trong ngày và cấp `QueueNumber`.
- `GET /api/appointments/waiting-queue?date=YYYY-MM-DD&doctorId=8`: lấy người đã check-in theo số thứ tự. Bác sĩ chỉ xem hàng của mình.
- `POST /api/appointments/call-next?date=YYYY-MM-DD&doctorId=8`: gọi bệnh nhân đầu hàng. Thao tác ghi `AppointmentStatusHistory`, giữ trạng thái lịch `Confirmed` để bác sĩ vẫn có thể bắt đầu khám theo flow hiện tại.
- `PUT /api/appointments/{id}/move-to-end`: chuyển người đã check-in xuống cuối hàng bằng cách cấp lại số lớn nhất trong ngày/bác sĩ và ghi lịch sử.
- `PUT /api/appointments/{id}/no-show`: lễ tân/Admin đánh dấu không đến sau giờ bắt đầu nếu chưa check-in.

Các thao tác gọi/chuyển hàng yêu cầu `Doctor`, `Receptionist` hoặc `Admin`; service vẫn kiểm tra bác sĩ chỉ được thao tác hàng của mình. Số thứ tự được cấp trong transaction hiện tại. Cơ chế realtime/WebSocket, phân trang và kiểm thử tải đồng thời SQL vẫn là phần mở rộng.

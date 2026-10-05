# VC-07 — Bàn tiếp đón và hàng chờ

- `GET /api/appointments/staff-appointments?date=YYYY-MM-DD&status=Confirmed&doctorId=8`: lễ tân/Admin xem lịch toàn phòng khám và có thể lọc bác sĩ; bác sĩ chỉ xem lịch của mình.
- `PUT /api/appointments/{id}/confirm`: xác nhận lịch `Pending`.
- `PUT /api/appointments/{id}/cancel`: hủy theo quyền vận hành và lưu lý do.
- `PUT /api/appointments/{id}/check-in`: check-in lịch đã xác nhận trong ngày, cấp số thứ tự.
- `PUT /api/appointments/{id}/no-show`: lễ tân/Admin đánh dấu `NoShow` sau giờ bắt đầu nếu bệnh nhân chưa check-in.
- `GET /api/appointments/waiting-queue?date=YYYY-MM-DD&doctorId=8`: lấy các lịch đã check-in theo số thứ tự, có thể lọc theo bác sĩ.

Các thao tác chuyển trạng thái đều ghi `AppointmentStatusHistory`. Phân trang/count và luồng đặt hộ cho khách vãng lai vẫn là phần mở rộng tiếp theo vì hợp đồng hiện tại đang trả danh sách tương thích với FE.

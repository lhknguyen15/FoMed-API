# VC-06 — Hóa đơn của tôi

- `GET /api/invoices?page=1`: bệnh nhân chỉ nhận hóa đơn có `patient.user_id` là tài khoản đang đăng nhập; lễ tân/Admin xem danh sách vận hành.
- `GET /api/invoices/{id}`: kiểm tra quyền sở hữu trước khi trả chi tiết.
- `POST /api/invoices`: lễ tân/Admin lập hóa đơn từ bệnh án đã hoàn tất.
- `POST /api/invoices/{id}/payments`: ghi nhận thanh toán một phần hoặc đủ; số tiền còn nợ được trả trong `remainingAmount`.

`InvoiceResponse` trả thêm `remainingAmount` và `statusName`. Trạng thái thanh toán một phần được suy ra từ `paidAmount` và số dư, không làm thay đổi enum SQL hiện có.

VC-11 bổ sung `GET /api/invoices/eligible`, lưu `consultationFee` từ `Appointment.FeeSnapshot`, và `POST /api/invoices/{id}/cancel` cho hóa đơn chưa phát sinh thanh toán. Chi tiết triển khai xem [vc-11-cashier](vc-11-cashier.md).

# VC-23 — Báo cáo

`GET /api/reports/summary?from=&to=&doctorId=` tổng hợp lượt hẹn, hoàn tất, no-show, hủy, tiền lập hóa đơn, đã thu và còn phải thu theo kỳ; có chi tiết theo bác sĩ. `GET /api/reports/export` dùng cùng bộ lọc và trả CSV để tải về. Receptionist và Admin được xem báo cáo.

Quy ước thời gian tài chính: doanh thu thực thu là tổng `Payment.Amount` có `PaidAt` nằm trong kỳ (không phụ thuộc hóa đơn được phát hành ngày nào); giá trị hóa đơn là hóa đơn phát hành trong kỳ; công nợ là số dư của hóa đơn chưa thu đủ (`Invoice.Status = 0`) được phát hành trong kỳ, tính `TotalAmount - tổng Payments`. Truy vấn công nợ lọc đồng thời `status` và `created_at` để tận dụng index `(status, created_at)`. Tỷ lệ không đến = lượt không đến / (tổng lượt hẹn - lượt đã hủy), làm tròn một chữ số thập phân.

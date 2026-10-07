# VC-23 — Báo cáo

`GET /api/reports/summary?from=&to=&doctorId=` tổng hợp lượt hẹn, hoàn tất, no-show, hủy, tiền lập hóa đơn, đã thu và còn phải thu theo kỳ; có chi tiết theo bác sĩ. `GET /api/reports/export` dùng cùng bộ lọc và trả CSV để tải về. Receptionist và Admin được xem báo cáo.

Quy ước thời gian tài chính: doanh thu thực thu là tổng `Payment.Amount` có `PaidAt` nằm trong kỳ (không phụ thuộc hóa đơn được phát hành ngày nào); giá trị hóa đơn là hóa đơn phát hành trong kỳ; công nợ là số dư của hóa đơn chưa thu đủ (`Invoice.Status = 0`) được phát hành trong kỳ, tính `TotalAmount - tổng Payments`. Truy vấn công nợ lọc đồng thời `status` và `created_at` để tận dụng index `(status, created_at)`. Tỷ lệ không đến = lượt không đến / (tổng lượt hẹn - lượt đã hủy), làm tròn một chữ số thập phân.

Tên bác sĩ được lấy từ hồ sơ của các bác sĩ có lịch hẹn, hóa đơn, khoản thu hoặc công nợ liên quan trong kỳ bằng một truy vấn ID/tên chung. Không phụ thuộc lịch khám trong kỳ và không loại bác sĩ đã ngừng hoạt động khỏi dữ liệu tài chính lịch sử. Vì vậy, bác sĩ có 0 lượt khám hôm nay vẫn có tên đầy đủ nếu hôm nay thu tiền cho hóa đơn cũ; không đổi ngày ghi nhận doanh thu để làm khớp số lượt khám. Nếu hồ sơ/tên không tồn tại, hiển thị “Chưa có thông tin bác sĩ” thay vì mã số. Dashboard, báo cáo lọc và CSV đều dùng cùng dữ liệu tổng hợp.

Kiểm thử cô lập: `dotnet run --project tests/Reports/Reports.csproj -c Release`. Bộ kiểm tra dùng dữ liệu trong RAM và dựng SQL không mở kết nối; không đọc cấu hình riêng hoặc thay dữ liệu local/Azure. Cần nghiệm thu lại tên bác sĩ trên Render sau lần deploy tiếp theo.

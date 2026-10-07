# Kiểm tra tên bác sĩ trong báo cáo

Chạy từ thư mục gốc FoMed:

```powershell
dotnet run --project tests/Reports/Reports.csproj -c Release
```

27 kiểm tra dùng trực tiếp `ReportService.SummaryAsync` và `ExportCsvAsync` với Entity Framework InMemory. Một kiểm tra bổ sung dựng SQL cho mẫu truy vấn lấy tên bằng `ToQueryString`, không mở kết nối. Không đọc appsettings, mật khẩu, gọi HTTP, kết nối SQL local/Azure hoặc ghi giao dịch thật.

Phạm vi: hóa đơn cũ thanh toán hôm nay nhưng không có lịch khám trong kỳ; bác sĩ ngừng hoạt động; hoạt động chỉ có hóa đơn/công nợ; không đưa bác sĩ không liên quan vào báo cáo; giữ thời gian ghi nhận tiền, số lượt khám, số liệu tiền mặt/SePay, công nợ, loại hóa đơn đã hủy, lọc bác sĩ, CSV tiếng Việt, quyền Admin/Receptionist và kết quả qua context mới.

Kiểm tra đầu tiên tái hiện lỗi tên trước bản sửa (không có lịch khám trong kỳ thì trả “Bác sĩ #8”), sau sửa toàn bộ kiểm tra đạt. Dữ liệu cố ý là demo; bản test không gọi provider SePay hay mở kết nối của chuỗi SQL giả.

Giới hạn: không phải SQL integration trên cơ sở dữ liệu thật, không gọi HTTP đã deploy, không chứng minh giao dịch cloud khác đã được nghiệm thu. Cần kiểm tra lại UI/dashboard/báo cáo và CSV sau khi commit/push/deploy được người dùng cho phép.

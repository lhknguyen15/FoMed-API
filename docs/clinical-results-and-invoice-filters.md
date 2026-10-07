# Theo dõi kết quả chỉ định và lọc thu ngân

## Bác sĩ

- Màn hình khám, chỉ định và kê đơn có bảng chỉ định/kết quả.
- Giữ ba trạng thái hiện có: **Chờ thực hiện**, **Đã có kết quả**, **Đã hủy**.
  Chưa có trạng thái nhận việc/đang thực hiện riêng cho kỹ thuật viên.
- Kết quả, kết luận, khoảng tham chiếu và thời gian ghi nhận hiển thị riêng.
- Nút **Cập nhật kết quả** chỉ tải chỉ định, không tải lại bệnh án/đơn thuốc;
  vì vậy không ghi đè nội dung bác sĩ đang nhập.
- Khi còn chỉ định chờ, tự tải lại mỗi 30 giây lúc trang đang được xem; dừng
  khi không còn chỉ định chờ hoặc trang bị ẩn. Khi lỗi, giãn lần tải tiếp theo
  và dừng sau ba lỗi liên tiếp; bác sĩ có thể bấm cập nhật lại.
- Sonner chỉ thông báo khi quan sát được chỉ định chuyển từ chờ sang có kết quả,
  không thông báo lại các kết quả đã có khi mở trang.
- Lỗi tải không được hiển thị thành “chưa có chỉ định” hay xác nhận hoàn thành.
- `GET /clinical/records/{id}/services` hiện có ghi nhật ký đọc bệnh án theo
  cơ chế sẵn có. Việc cập nhật không sửa kết quả, đơn thuốc hoặc khoản thu.

## Thu ngân

Endpoint mới: `GET /api/invoices/search`, chỉ cho lễ tân/quản trị viên đang hoạt động.
Không thay đổi hợp đồng danh sách `/api/invoices` dùng bởi các màn hình khác.

Tham số:

| Tham số | Ý nghĩa |
| --- | --- |
| `keyword` | Tối đa 100 ký tự; mã hóa đơn, tên trên hóa đơn, mã/số bệnh nhân |
| `status` | `all`, `outstanding`, `unpaid`, `partial`, `paid`, `cancelled` |
| `fromDate`, `toDate` | Ngày lập, định dạng `yyyy-MM-dd`, tính theo UTC+7; gồm cả hai ngày |
| `page` | Từ 1 đến 100000; cố định 20 kết quả/trang |

Tên bệnh nhân ưu tiên bản lưu tại thời điểm lập hóa đơn; nếu chưa có, dùng tên hồ sơ.
Tổng đã thu lấy từ các khoản thanh toán đã lưu, không dùng tiền khách đưa.
Lọc tại SQL trước đếm/phân trang, sắp ngày lập giảm dần rồi ID giảm dần.
Kết quả gồm `items`, `page`, `pageSize`, `totalCount` và thời gian UTC có múi giờ.
Không tạo/sửa hóa đơn hoặc thanh toán và không cần migration database mới.

Frontend áp dụng bộ lọc khi bấm **Áp dụng**, đặt lại trang 1, giữ bộ lọc trong URL.
Ngày lập hiển thị giờ Việt Nam. Bộ lọc dành cho tab **Hóa đơn đã lập**; tab
**Chờ lập hóa đơn** vẫn giữ quy trình riêng, không lọc giả trên một trang dữ liệu.
Nếu API chưa triển khai endpoint mới, hiển thị lỗi/thử lại, không âm thầm trả
danh sách cũ không được lọc.

## Kiểm thử và nghiệm thu

Ngoại tuyến, không đọc cấu hình bí mật hoặc gọi Azure:

```powershell
dotnet run --project tests/InvoiceSearch
# Trong repository FoMed-Frontend:
node tests/service-order-results.audit.cjs
node tests/invoice-filters.audit.cjs
npm run lint
npm run build
```

Các kiểm thử dùng dữ liệu giả/in-memory, có kiểm tra dịch truy vấn SQL Server
bằng `ToQueryString()` mà không mở kết nối. Đây không phải nghiệm thu cloud.

Sau khi được phép commit/push và merge, triển khai API Render trước, frontend
Vercel sau. Cần nghiệm thu với hồ sơ DEMO được cho phép thao tác:

1. Bác sĩ tạo chỉ định, mở màn hình khám/kê đơn và nhập nội dung nháp.
2. Kỹ thuật viên lưu kết quả. Bác sĩ xem cập nhật tự động/nút cập nhật, kiểm tra
   thông báo, đủ kết quả/kết luận và nội dung nháp không bị mất.
3. Kiểm tra nhiều chỉ định còn chờ, chỉ định hủy và lỗi mạng; không được báo
   hoàn thành tất cả chỉ định chỉ vì có một kết quả.
4. Thu ngân tìm hóa đơn nằm ngoài trang đầu; lọc còn phải thu, thanh toán một phần,
   đã thanh toán/hủy và ngày lập. Đối chiếu tổng số, trang cuối, xóa lọc.
5. Mở một hóa đơn rồi quay lại; bộ lọc/trang được giữ theo lịch sử điều hướng.

Không chạy kiểm thử ghi trên hồ sơ thật, không gửi giao dịch SePay thật.

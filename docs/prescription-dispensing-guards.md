# Khóa cấp phát và chỉnh sửa đơn thuốc — mục 1

## Nghiệp vụ đã triển khai

- Chỉ cấp phát khi `medical_record.IsFinalized = true` **và** lịch hẹn `Status = Completed (3)`.
- Thiếu một trong hai điều kiện: HTTP 409 với thông báo “Bệnh án chưa chốt hoặc lượt khám chưa hoàn tất, chưa thể phát thuốc.”; không trừ tồn, không ghi phân bổ hoặc xuất kho.
- Đơn đã cấp phát một phần hoặc toàn bộ không được chỉnh sửa. Kiểm tra diễn ra **trước** câu lệnh xóa dòng đơn, trong cùng write transaction.
- Giữ bảo vệ dữ liệu cũ dùng `batch_id` và giao dịch OUT âm có `ref_id` là đơn thuốc, kể cả khi chưa được backfill vào `prescription_dispenses`.
- FEFO, kiểm tra thiếu tồn và idempotency giữ nguyên. Hai yêu cầu phát cùng đơn đồng thời chỉ trừ đúng số lượng một lần.
- **Chưa thêm** quy tắc phải thanh toán đủ trước phát thuốc. Đây là quyết định nghiệp vụ riêng, không suy ra từ VC-16.

Không thay đổi schema; không có migration/seed nào cần chạy cho mục này. Không sửa/xóa dữ liệu cấp phát cũ để làm hết lỗi.

## API/UI

| Hợp đồng | Thay đổi |
| --- | --- |
| `POST /api/pharmacy/prescriptions/{id}/dispense` | Kiểm tra trạng thái khám/chốt trước khi xử lý tồn; 409 rõ ràng; replay đơn đã phát đủ vẫn không trừ kho lần hai |
| `PUT /api/clinical/records/{id}/prescription` | Chặn sửa đơn có lịch sử cấp phát trước `ExecuteDeleteAsync`; không còn rơi vào lỗi FK 500 của trường hợp đơn nháp đã phát |
| `GET /api/clinical/records/{id}/prescription` | Thêm `isDispensed` để bác sĩ hiển thị chế độ chỉ đọc ngay khi tải trang |
| `GET /api/pharmacy/prescriptions/{id}` — mới | Chỉ Pharmacist/Admin; trả danh sách thuốc, SL kê/đã phát, bệnh nhân/mã BN/bác sĩ và điều kiện cấp phát; không trả toàn bộ bệnh án/chẩn đoán |

Endpoint đọc cho dược sĩ trả `prescriptionId`, `medicalRecordId`, `patientName`, `patientCode`, `doctorName`, `isFinalized`, `appointmentStatus`, `isDispensed`, `isFullyDispensed`, `canDispense`, `blockedReason`, `items`.

`canDispense` là điều kiện bệnh án/đơn, **không phải cam kết còn đủ tồn**. Tồn được kiểm tra tại transaction xác nhận; preview lô/tồn khả dụng đầy đủ thuộc mục triển khai tiếp sau.

- `/pharmacy/dispense`: nhập mã → **Tra cứu đơn** → xem bệnh nhân/thuốc/trạng thái → **Xác nhận phát thuốc**. Nút disabled nếu chưa chốt hoặc đã phát đủ. Đổi mã tìm kiếm bỏ nội dung/nút của đơn cũ, tra cứu không có dữ liệu hiện thông báo và Thử lại.
- Sau xác nhận thành công: hiển thị các lô đã xuất, khóa submit lặp và đọc lại trạng thái. Xung đột/lỗi hiện thông báo API và đọc lại trạng thái, không tự gửi lại POST.
- Trang đơn thuốc bác sĩ: khóa form và hiện lý do nếu đã cấp phát, kể cả dữ liệu cũ chưa chốt. Khi lưu nhận 409, đọc lại bệnh án/đơn để cập nhật quyền chỉnh sửa.
- UI dùng AppShell/Card/Button/Badge hiện có, bảng cuộn ngang bên trong trên mobile.

## Kiểm thử

Kết quả lượt kiểm tra mục 1: **93 assertions HTTP đạt**, **120 assertions browser đạt**; runner cộng 2 assertions xác nhận browser nên báo 95/0. Regression SQL 51 checks và bộ phân quyền/auth hiện có đều đạt; FE lint, TypeScript, Vite build và API build thành công. Cảnh báo dependency NU1903/bundle lớn có sẵn vẫn còn, chưa xử lý trong mục này.

Runner HTTP/JWT `tests/ClinicWorkflow/HttpWorkflowAudit.cs` bổ sung:

1. Cấp phát bệnh án nháp bị 409 và bảo toàn toàn bộ tồn/phân bổ/giao dịch.
2. Chỉ có cờ chốt hoặc chỉ có lịch Completed vẫn không được cấp phát.
3. Đơn nháp chưa phát vẫn sửa được.
4. Đơn nháp đã phát một phần bị khóa; 409 giữ nguyên dòng, ghi chú, tồn và lịch sử.
5. Đơn legacy batch/OUT cũng bị khóa.
6. Thuốc ở nhiều dòng bị thiếu tồn không xuất một phần rồi bỏ dở.
7. Cấp phát đồng thời một lần thực hiện/một lần replay; số lượng xuất đúng.
8. API đọc dược sĩ từ chối Patient/Doctor và trả 404 cho đơn không tồn tại.
9. UI thực hiện xác nhận trên API thật; sau thành công không thể submit lần hai.

Chạy từ repository gốc sau build API/test vào output riêng:

```powershell
dotnet build FoMed-API/FoMed.Api/FoMed.Api.csproj --no-restore -o FoMed-API/FoMed.Api/bin/WorkflowAudit
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj --no-restore -o tests/ClinicWorkflow/bin/HttpAudit
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http --dispensing-only
```

Thêm `--browser` với preview 5184 proxy sang API fixture 5181 và `PLAYWRIGHT_MODULE` như hướng dẫn trong báo cáo audit. Runner tự tạo/xóa database riêng; không chạy script browser trực tiếp trên database ứng dụng.

Ở lượt nghiệm thu ban đầu mục 1, `--dispensing-only` chưa kiểm tra các lỗi ngoài phạm vi mục 1. Sau mục 2, fixture đã cập nhật: hết tồn/vượt tồn luôn được kiểm tra; flag này vẫn bỏ nhật ký ghi bệnh án và assertion dị ứng của trang chỉ đọc. Dùng `--prescribing-only --browser` để kiểm tra cả tồn và dị ứng; chỉ bỏ assertion nhật ký ghi thuộc mục 3. Chạy không có flag phạm vi để theo dõi backlog toàn bộ. Chi tiết kết quả mới nằm trong `docs/prescription-search-stock-allergies.md`.

Sau cập nhật, cần **khởi động lại FoMed-API bằng source mới** để frontend dùng được endpoint/metadata mới. API đang chạy trước đó không tự nhận thay đổi từ binary kiểm thử riêng.

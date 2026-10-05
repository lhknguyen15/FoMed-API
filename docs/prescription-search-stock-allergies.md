# Tìm thuốc, tồn khả dụng và dị ứng từ server — mục 2

## Phạm vi

Dựa trên VC-15 và các bảng/API Clinical, Pharmacy hiện có. Không dùng FoMed-FE. Giữ nguyên cấu trúc features/workspaces của FoMed-Frontend; không tạo module thay thế.

Không thay đổi schema, không cần chạy SQL. Cần khởi động lại FoMed-API từ source mới; API đang chạy không tự nhận binary kiểm thử riêng.

## API

- `GET /api/clinical/medicines/search?recordId=123&keyword=para&page=1`: Doctor-only, kiểm tra bác sĩ phụ trách bệnh án; trả `items`, `page`, `pageSize=20`, `totalCount`. Từ khóa tối đa 100 ký tự, tìm theo tên thuốc; chỉ thuốc đang hoạt động.
- Mỗi thuốc gồm `id`, `name`, `unit`, `price`, `availableQuantity`. Thuốc tồn 0 vẫn được trả để UI giải thích vì sao không chọn được.
- `GET /api/clinical/records/{id}/prescribing-context`: Doctor-only và quyền phụ trách; trả `medicalRecordId`, `patientName`, `allergies`, `medicines` hiện có trong đơn. Có thể gửi thêm query `medicineIds=1&medicineIds=2` (tối đa 100 ID dương) để cập nhật tồn các dòng đang nhập nhưng chưa lưu.
- GET context ghi nhật ký Read bệnh án. Không mở danh sách dị ứng cho bác sĩ khác bằng cách truyền patientId. Đường dẫn triển khai nằm trong controller Clinical hiện có, không giả định endpoint `/api/medicines/search` của bản review đã tồn tại.

## Quy tắc tồn

Tồn khả dụng = tổng số lượng dương của các lô có `expiry_date >= ngày hiện tại tại phòng khám (UTC+07)`; quy tắc hết hạn theo ngày giống cấp phát hiện có. Ngày hết hạn được tính đến cuối ngày đó.

POST và PUT đơn thuốc kiểm tra lại số lượng từng thuốc trong write transaction dùng khóa database chung với xuất/điều chỉnh kho. Thiếu tồn trả 409, thông báo tên thuốc và tồn còn lại. Khi PUT thất bại, transaction rollback bảo toàn đơn cũ, ghi chú và snapshot; không để các dòng bị xóa dở.

Kê đơn **không trừ tồn/không giữ chỗ**. Nhiều đơn chưa cấp phát có thể cùng tham chiếu một tồn; API phát thuốc tiếp tục kiểm tra lại và FEFO. Đây không phải cơ chế reservation.

## UI bác sĩ

- Tìm thuốc theo tên, phân trang 20 thuốc; hiển thị đơn vị/giá/tồn. Không còn select chỉ lấy 20 thuốc đầu.
- Không chọn thuốc hết tồn hoặc thêm trùng thuốc; kiểm tra số lượng nguyên dương, liều dùng, xác nhận dị ứng và vượt tồn trước lưu. API vẫn là chốt kiểm tra cuối cùng.
- Kết quả tìm kiếm giới hạn chiều cao, cuộn bên trong; form một cột trên mobile, giữ AppShell/Card/Button/Badge hiện có.
- GET context cung cấp dị ứng cả ở trang khám và kê đơn; không dùng location.state làm nguồn dị ứng. Tải lại/deep link vẫn hiển thị đúng.
- Không tải được context: thông báo chưa lấy được dị ứng, khóa lưu, có nút tải lại; không hiển thị kết luận không dị ứng.
- Khi lỗi tồn 409: cập nhật tồn của dòng đã chọn và giữ nội dung chưa lưu; không tự gửi lại POST/PUT. Khi đã cấp phát: giữ cơ chế khóa chỉnh sửa của mục 1.
- Đơn chỉ đọc hiển thị giá snapshot, không lấy giá hiện tại của danh mục làm giá đã kê.

## Giới hạn cần nêu rõ

`Patient.Allergies` hiện là chuỗi tự do; `Medicine` chưa có trường hoạt chất cấu trúc. Chưa có đối chiếu dị ứng theo từng hoạt chất như bản review. Không suy đoán hoạt chất từ tên thuốc, không đánh dấu thuốc an toàn bằng việc không khớp chuỗi. Bác sĩ vẫn phải kiểm tra và xác nhận theo hợp đồng `allergyAcknowledged` hiện có.

Attachment, PDF đơn chuyên dụng, reservation, audit ghi bệnh án và preview lô đầy đủ không thuộc mục này.

## Kiểm thử

Runner `tests/ClinicWorkflow` tạo/xóa database riêng, không sửa FoMedDb. Ca kiểm tra gồm hết tồn, chỉ có lô hết hạn, vượt tồn khi tạo/cập nhật, rollback đơn cũ, không trừ kho khi kê, phân quyền, tìm kiếm/trang 2, context thuốc chưa lưu; browser gọi POST/PUT thật, reload, chọn trùng, xác nhận dị ứng, tồn thay đổi sau khi tải và lỗi tải context.

Build API/tests vào output riêng như báo cáo audit, build FE rồi chạy preview 5184 proxy sang fixture 5181. Sau đó chạy từ repository gốc:

```powershell
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http --prescribing-only --browser
```

Flag `--prescribing-only` chỉ bỏ kiểm tra nhật ký **ghi** bệnh án còn thuộc mục 3. Các kiểm tra tồn/dị ứng và regression cấp phát đều chạy. Không coi toàn bộ audit đã hoàn thành chỉ vì lượt chạy này đạt.

Lượt cuối: **119 assertions HTTP đạt, 138 assertions UI đạt**; runner thêm 3 kiểm tra đối chiếu browser/database nên báo **122/0**. Regression SQL 51 checks, phân quyền/auth, lint, TypeScript, API và Vite build đều đạt. Các ca UI có chờ phản hồi/trạng thái render, không dựa vào việc click xong là dữ liệu đã cập nhật. Cảnh báo dependency NU1903 và bundle lớn có sẵn vẫn còn.

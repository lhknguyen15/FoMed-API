# Giao dịch tiền mặt: tiền khách đưa, tiền thừa và người thu

## Quyết định nghiệp vụ

- UI thu ngân chỉ thanh toán đủ số dư. Tiền mặt cho phép đưa đủ hoặc dư; thiếu tiền bị chặn ở UI. API thu một phần vẫn giữ nguyên để phát triển sau.
- `payments.amount` là tiền thực thu vào hóa đơn, dùng cho doanh thu. Không ghi toàn bộ tiền khách đưa vào `amount`.
- Lưu `cash_received` theo từng payment. Không lưu cột tiền thừa độc lập: server tính `changeAmount = cashReceived - amount`.
- Lưu `received_by` từ tài khoản đã xác thực và `received_by_name_snapshot` tại thời điểm thu. Không nhận người thu hay tiền thừa do frontend tự gửi.
- Giao dịch cũ giữ NULL, không suy đoán tiền khách đưa/người thu từ số tiền hoặc người lập hóa đơn.
- Đây không phải nghiệp vụ hoàn tiền. Số tiền thừa là giá trị đối chiếu; chưa ghi nhận một bước riêng xác nhận đã trao tiền thừa thực tế cho khách.

## Áp dụng database trước khi chạy API mới

Sao lưu và kiểm tra đúng database, sau đó execute:

`database/migrations/20261005_add_payment_cash_audit.sql`

File có `USE [FoMedDb]`, thêm 4 cột nullable vào `billing.payments`, FK người thu, check constraint tiền khách đưa và unique index `(received_by, idempotency_key)` với key khác NULL. Có thể chạy lại mà không tạo cột/index trùng, không cập nhật khoản thu cũ. Tạo database mới từ script gốc vẫn cần chạy các migration.

Không tự thực thi file này lên FoMedDb trong lượt triển khai. Nếu chạy API mới trước migration, các truy vấn payment sẽ lỗi thiếu cột. Sau migration cần khởi động lại API với bản build mới.

## Hợp đồng API

`POST /api/invoices/{id}/payments`, Receptionist/Admin:

```json
{
  "amount": 350000,
  "method": 0,
  "cashReceived": 500000,
  "note": "Thu tiền mặt tại quầy",
  "idempotencyKey": "73e7d884-54ad-42ed-8c91-785529b98547"
}
```

- `amount`: dương, tối đa 9.999.999.999,99, tối đa 2 chữ số thập phân, không vượt số dư.
- `method`: 0 tiền mặt, 1 thẻ, 2 chuyển khoản, 3 ví điện tử.
- `cashReceived`: optional để tương thích client cũ. Khi có, chỉ dùng với tiền mặt, là số đồng nguyên dương ≤ 10 tỷ và ≥ `amount`. Không tự mặc định bằng `amount` khi thiếu.
- Không gửi `cashReceived` cho thẻ/chuyển khoản/ví. Tiền thừa và tiền khách đưa trong response là NULL cho các phương thức này.
- `idempotencyKey`: UUID optional để tương thích client cũ; UUID rỗng/không hợp lệ bị từ chối. Frontend mới luôn gửi UUID.
- `receivedBy`, `receivedByName`, `changeAmount` không thuộc request; backend tự ghi nhận/tính.

Payment trong response POST, GET chi tiết và GET danh sách hóa đơn có thêm `cashReceived`, `changeAmount`, `receivedBy`, `receivedByName`, `idempotencyKey`. `paidAt` trả UTC có `Z` để hiển thị đúng sau tải lại.

Ví dụ: hóa đơn 350.000, khách đưa 500.000 → `amount=350000`, `cashReceived=500000`, `changeAmount=150000`, doanh thu 350.000. Công nợ vẫn tính theo tổng hóa đơn trừ tổng payment.amount, không đổi theo tiền khách đưa.

## Chống ghi nhận lặp

Phạm vi khóa là **tài khoản người thu đã xác thực + UUID**:

- Cùng key, cùng invoice/amount/method/cashReceived/ghi chú đã chuẩn hóa: trả hóa đơn hiện tại có giao dịch đã ghi, không thêm payment; kể cả invoice đã thanh toán xong.
- Cùng key nhưng khác nội dung hoặc hóa đơn: HTTP 409, không ghi thêm.
- Transaction/write lock hiện có và unique index bảo vệ cả yêu cầu đồng thời, không chỉ double-click trên UI.
- Request không có key vẫn theo workflow cũ; không được coi là có bảo đảm idempotency.

FE giữ key trong lượt thao tác đang mở để retry payload không đổi; sửa payload tạo key mới. Sau lỗi tải lại hóa đơn. Nếu tìm được giao dịch đúng key/amount/method/tender, hiện thông báo đã đối soát, lịch sử đã lưu và không thu lại. Key chưa có cơ chế lưu bền qua đóng tab/reload; FE phải đọc số dư/lịch sử mới trước thao tác tiếp, không tự phát lại request cũ. API luôn kiểm tra số dư/trạng thái.

## UI

`features/billing/components/PaymentHistory.tsx` được dùng ở thu ngân và modal hóa đơn bệnh nhân. Hiển thị khoản thu vào hóa đơn, phương thức/ngày, người thu, tiền khách đưa và tiền thừa từ response API. Lịch sử dài có vùng cuộn, bố cục đáp ứng mobile. Bệnh nhân chỉ được xem hóa đơn của mình theo kiểm tra API hiện có. Giao dịch cũ hiển thị chưa ghi nhận, không tạo số tiền hoặc danh tính giả.

Ô nhập tiền vẫn dùng parser phân nhóm Việt Nam: `500000`, `500.000`, `500 000` hợp lệ; không chuyển `150.000` thành 150. Tiền thừa dự kiến phía UI không thay thế dữ liệu giao dịch do server xác nhận.

## Kiểm thử

Kết quả lượt cuối: **546 assertions / 130 trường hợp UI mock; 329 UI với API thật (178 smoke + 151 E2E); runner 295 đạt/0 lỗi (271 HTTP/SQL, trong đó 45 cash audit, + 24 browser/database); SQL regression 51, time/slot 7**. Build API/test runner, TypeScript, lint, Vite đạt. Không gộp lượt trung gian lỗi selector/chờ request của test vào kết quả này; các lỗi đó đã sửa và chạy lại toàn bộ. Cảnh báo `System.Security.Cryptography.Xml` 9.0.0 và bundle lớn có sẵn chưa được xử lý. API/preview/DB/kho file kiểm thử đã dọn. Chi tiết tại `FoMed-Frontend/docs/MONEY-INPUT-AUDIT.md` và `docs/clinical-workflow-item5.md`.

- `tests/ClinicWorkflow/PaymentCashAudit.cs`: API thật/JWT/SQL trên database ngẫu nhiên; dữ liệu giả có chủ ý. Kiểm tra dữ liệu cũ, validation, giả mạo người thu, partial API, noncash, snapshot tên dài, quyền bệnh nhân, replay/đồng thời, constraint/index và migration chạy lại.
- `FoMed-Frontend/tests/cashier-money-input.audit.cjs`: toàn bộ API mock, không forward; kiểm tra định dạng, payload, busy guard, retry key, reload/lost-response và layout desktop/mobile. Không coi kết quả này là thanh toán thật.
- Luồng đa role qua `--http --browser`: API/DB tạm thật; có ca mất phản hồi **sau** server commit, đối soát UI và kiểm tra đúng một payment, doanh thu không tính tender/change.

Chưa nghiệm thu Safari/Firefox, bàn phím điện thoại thật, vận hành quầy tiền mặt, các luồng đổi/hoàn tiền, hoặc migration trên database triển khai thực tế. Không commit/push tự động.

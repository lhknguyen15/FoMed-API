# SePay ngân hàng / Webhooks — backend FoMed

## Phạm vi hiện tại

Tích hợp QR ngay trong FoMed, **không phải Cổng thanh toán / Merchant ID / Secret Key Sandbox**. Backend đã có tạo yêu cầu QR, đọc trạng thái, nhận webhook HMAC và danh sách giao dịch đối soát cho Admin. UI thu ngân đã nối tạo yêu cầu/poll trạng thái, cảnh báo Test mode và lịch sử nguồn giao dịch; xem [hướng dẫn frontend](../FoMed-Frontend/docs/SEPAY-CASHIER.md). Hướng triển khai đã chọn là **Render + Azure SQL**, webhook nhận trực tiếp tại API HTTPS trên Render. Chưa deploy Render, chưa nối thanh toán vào UI bệnh nhân, chưa nhận webhook từ dashboard SePay và chưa thử tiền thật.

SePay mặc định tắt. Test mode phải dùng database chỉ chứa dữ liệu giả và khai báo `SePay:TestDatabaseName` khớp tên DB thực tế; DB hệ thống bị chặn. Có thể dùng `FoMedDb` sau khi người vận hành xác nhận toàn bộ dữ liệu là demo, không còn cấm theo tên. Tên database không tự bảo đảm dữ liệu là giả. Không bật Test trên bản sao chứa dữ liệu bệnh nhân/thanh toán thật. `Live` cần bật `AllowLivePayments=true` rõ ràng và không trỏ vào tên DB đã khai báo demo. Mỗi instance chỉ cấu hình một công ty/tài khoản nhận SePay; không dùng chung URL/secret Test và Live. Xem [quy trình deploy Render + Azure SQL](deploy-render-azure.md).

## API

| Endpoint | Quyền và dữ liệu |
| --- | --- |
| `POST /api/invoices/{id}/sepay/payment-requests` | Receptionist/Admin/Patient; bệnh nhân chỉ hóa đơn của mình. Không nhận amount từ client. Trả HTTP 200 cả khi tạo mới hoặc dùng lại yêu cầu còn hiệu lực. |
| `GET /api/invoices/{id}/sepay/payment-requests/{requestId}` | Cùng quyền sở hữu; request phải thuộc hóa đơn và môi trường đang cấu hình. |
| `POST /api/webhooks/sepay` | Không dùng JWT FoMed; bắt buộc HMAC-SHA256, timestamp. Chỉ JSON, body tối đa 16 KiB. |
| `GET /api/sepay/transactions?status=ReviewRequired&page=1` | Admin; 20/trang, `status` optional: Applied/ReviewRequired/Ignored. Không trả raw content/tài khoản khách/secret. Hiện là API tra cứu, chưa có UI hoặc chức năng xử lý đối soát. |

API người dùng dùng `HTTPResponseData` như hóa đơn hiện tại. Webhook thành công phải trả trực tiếp **HTTP 200 + `{"success":true}`**, không bọc response theo hợp đồng FoMed.

Tạo QR tính `totalAmount - SUM(payments.amount)` tại server dưới cùng write lock với thu tiền mặt/hủy hóa đơn. Hóa đơn đã thu/hủy hoặc số dư có phần thập phân bị 409; không làm tròn tiền. Một yêu cầu Pending/môi trường/hóa đơn; gọi lại trả cùng ID nếu tiền/tài khoản/thời hạn không đổi. Mã `FM` + 24 ký tự hex ngẫu nhiên, không chứa tên bệnh nhân, bệnh án hay chẩn đoán. Thời hạn mặc định 15 phút.

Response gồm ID, invoiceId, environment, code, amount, ngân hàng/tài khoản/tên thụ hưởng, createdAt/expiresAt UTC có Z, status, remainingAmount và qrUrl. QR dùng `https://vietqr.app/img` theo tài liệu SePay; backend chỉ tạo URL, không gọi dịch vụ ngoài. Frontend thu ngân hiển thị rõ Test mode và không tải/hiển thị QR trong môi trường Test, dùng code/amount để mô phỏng; Live chỉ hiển thị khi URL khớp các trường response đã kiểm tra.

Trạng thái yêu cầu: Pending/Paid/Expired/Superseded/ReviewRequired. GET còn có InvoiceSettled/InvoiceCancelled khi hóa đơn đóng bởi luồng khác. Chỉ Pending có qrUrl; frontend dùng trạng thái server, không tự xác nhận thành công khi người dùng bấm đã chuyển. `Paid` nghĩa là yêu cầu này đã có payment; InvoiceSettled là hóa đơn được thanh toán qua luồng khác.

## Webhook, bảo mật và tiền

- Chữ ký: `X-SePay-Signature: sha256={hex}`; timestamp Unix giây ở `X-SePay-Timestamp`. Tính HMAC trên bytes gốc `{timestamp}.{raw_body}`, so sánh constant-time; sai/thiếu/hết cửa sổ ±300 giây trả 401. Không reserialize JSON. Từ chối tên trường JSON trùng, kể cả khác hoa/thường.
- Payload đọc `id`, gateway, accountNumber, code, content, transferType, transferAmount, transactionDate, referenceCode. `id` phải dương; số tiền là đồng nguyên dương trong giới hạn lưu trữ; timestamp giao dịch định dạng giờ Việt Nam được đổi UTC. Không dùng accumulated làm doanh thu. Không đọc invoiceId/userId từ webhook.
- Kiểm tra ngân hàng/tài khoản nhận theo cấu hình; BankCode cho ảnh QR và Gateway từ webhook là hai giá trị có thể khác nhau (ví dụ MB / MBBank). Không đoán alias tự động.
- Tìm mã đúng định dạng từ code hoặc một mã duy nhất trong content. Không dùng so khớp một phần. Nhiều mã hoặc code/content mâu thuẫn được lưu đối soát.
- ID giao dịch duy nhất theo môi trường; unique index ở receipt và payment, transaction/write lock SQL bảo vệ giữa nhiều process. Replay cùng nội dung kinh tế trả thành công, không thêm payment; JSON khác khoảng trắng không gây thu trùng. ID đã nhận nhưng nội dung khác trả 409, không thay khoản thu cũ.
- Chỉ tự thu khi yêu cầu Pending, chưa hết hạn tại thời điểm xử lý, giao dịch không có trước yêu cầu, hóa đơn còn mở, số dư chưa đổi và **số tiền đúng số dư**. Payment method=2, provider=SePay, môi trường và transaction ID; người thu/cashReceived/idempotency key người thu là NULL, không gán người tạo QR thành người thu. Payment paidAt lấy giờ giao dịch ngân hàng, không phải lúc poll.
- Receipt + payment + trạng thái hóa đơn + yêu cầu được commit cùng transaction, rồi mới ACK. Lỗi database không ACK thành công; retry thực hiện lại được. Không dùng queue in-memory hoặc ACK trước khi lưu bền vững.
- Sai tài khoản/mã, thiếu/dư tiền, QR hết hạn/đã thay thế, hóa đơn đóng hoặc số dư thay đổi: lưu receipt ReviewRequired và lý do, không ghi payment/không tự chốt. Giao dịch tiền ra là Ignored. Giao dịch muộn vào QR cũ cũng khóa QR mới của cùng hóa đơn để chờ đối soát. Không tự xử lý hoàn tiền, không coi dư chuyển khoản là đã thối tiền.
- Receipt ACK chỉ xác nhận đã lưu giao dịch, **không khẳng định hóa đơn đã thanh toán**. Giao dịch cần đối soát hiện chưa có API phân bổ/hoàn tiền/mở khóa; cần triển khai nghiệp vụ có phân quyền và audit riêng trước vận hành thật. Không xóa hoặc sửa trực tiếp receipt để giả lập hoàn tiền.

Báo cáo hiện tại vẫn tính từ payments: chỉ tiền được phân bổ vào hóa đơn tham gia doanh thu/công nợ. Tiền ngân hàng đã về nhưng chưa phân bổ nằm ở ledger đối soát, **không biến mất**, không được diễn giải báo cáo hóa đơn là toàn bộ số dư ngân hàng. Cần bổ sung báo cáo tiền chờ đối soát trước vận hành thật. Không đưa nội dung ngân hàng vào log công khai; bảng receipt có content cần quyền SQL, retention và bảo vệ backup phù hợp.

## Migration và cấu hình

1. Sao lưu, kiểm tra đúng database. Chạy migration cash audit trước nếu chưa có, rồi `database/migrations/20261005_add_sepay_payments.sql`. File mặc định có `USE [FoMedDb]`; chỉ dùng khi đó đúng là DB mục tiêu. Nếu thử trên DB khác, đổi database có chủ ý trong bản script được kiểm soát. Test yêu cầu dữ liệu giả và xác nhận `TestDatabaseName`, kể cả khi DB tên FoMedDb. Các test runner tự loại bỏ/chặn chuyển database và chỉ chạy trên DB tạm.
2. Migration thêm 3 cột provider nullable vào payments, hai bảng request/receipt, FK, check và unique index; chạy lại được, không backfill hoặc suy đoán nguồn giao dịch cũ. **API mới cần migration dù SePay tắt**, vì model Payment đã có các cột mới. Không chạy tự động lên database ứng dụng trong lượt triển khai này.
3. File `FoMed.Api/appsettings.SePay.example.json` chỉ là mẫu (Enabled=false, không có secret) và không tự được load. Không copy secret thật vào file tracked hoặc vào VITE_*.
4. Cấu hình `SePay:*` và ConnectionStrings bằng .NET User Secrets (Development, UserSecretsId đã có), biến môi trường của process/server hoặc secret manager. User Secrets tránh Git nhưng không mã hóa tại ổ đĩa; không gửi nội dung secrets vào chat.

Các khóa cần thiết:

| Khóa | Giá trị |
| --- | --- |
| `SePay:Enabled` | true khi DB test/migration/cấu hình đã sẵn sàng |
| `SePay:Environment` | Test |
| `SePay:AllowLivePayments` | false |
| `SePay:TestDatabaseName` | tên chính xác DB demo đã xác nhận; phải khớp connection string |
| `SePay:BankCode` | Mã ngân hàng QR theo tài khoản test |
| `SePay:Gateway` | gateway chính xác trong webhook test |
| `SePay:AccountNumber` / `SePay:AccountName` | Tài khoản/tên thụ hưởng test |
| `SePay:WebhookSecret` | Secret HMAC tạo trong dashboard, tối thiểu 32 ký tự, không dùng secret audit trong test source |
| `ConnectionStrings:DefaultConnection` | Trỏ về database demo đã kiểm tra nội dung và chuẩn bị migration |

Biến môi trường thay dấu `:` bằng `__`, ví dụ `SePay__WebhookSecret`. Có thể quản lý User Secrets trong IDE; không nhập secret thật vào lệnh lưu lịch sử shell. Khởi động lại API sau cấu hình.

5. Kiểm tra endpoint local bằng bộ test gọi trực tiếp API, rồi deploy môi trường demo theo [Render + Azure SQL](deploy-render-azure.md). Giữ SePay tắt trong lần deploy đầu; chỉ bật Test sau khi database, credentials và API cloud được nghiệm thu. URL nhận webhook là `https://<service-thuc-te>.onrender.com/api/webhooks/sepay`, không phải URL frontend hoặc localhost.
6. Dashboard Test mode → Webhook: Tiền vào, JSON, bật gửi lại, đúng tài khoản test, HMAC-SHA256 với secret khớp backend. Ban đầu không bật bộ lọc bỏ giao dịch không có mã để giữ được ca đối soát; cấu hình nhận diện tiền tố FM nếu dùng code. Nếu code=null backend vẫn kiểm tra một mã FM chính xác trong content.
7. Tạo yêu cầu thanh toán từ API, lấy code/amount rồi mô phỏng giao dịch ở SePay. Chỉ poll API trạng thái của FoMed. Trong Live cần HTTPS/trusted proxy, logging bảo mật, giám sát lỗi và kiểm thử tải; không công khai Swagger/cấu hình phát triển trên Internet.

## Kiểm thử và giới hạn

Chạy từ root sau khi SQL Server local sẵn sàng:

```powershell
dotnet build FoMed-API/FoMed.Api/FoMed.Api.csproj --no-restore -o FoMed-API/FoMed.Api/bin/WorkflowAudit
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj --no-restore -o tests/ClinicWorkflow/bin/HttpAudit
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http --sepay-only
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --sql
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http --cash-only
```

Runner chỉ dùng credentials giả trên process local, database `FoMed_Audit_<guid>` và cổng 5181 đã kiểm tra trống; không gửi dữ liệu tới SePay. Có HTTP thật/JWT/HMAC/SQL, replay/đồng thời, DB fault rollback/retry, quyền sở hữu, thiếu/dư tiền, QR cũ/hết hạn/hủy, cash race, source payment và báo cáo. Có kiểm tra chữ ký trên bytes tiếng Việt, tương thích migration chạy lại. Database/file/process kiểm thử tự dọn.

Lượt SePay trực tiếp trước đó đạt **129 kiểm tra HTTP/SQL (gồm 9 login fixture)**, cùng **31 kiểm tra cấu hình/chữ ký và 7 time/slot** (06/10/2026). SQL regression trước đó đạt **51**, cash riêng **54 HTTP/SQL (9 login + 45 cash audit)**, regression phân quyền/auth đạt. Lượt HTTP toàn workflow trước đó dừng sau **132 checks đạt** do fixture cần ít nhất 5 slot tương lai trong ngày; không ghi nhận là full regression đạt. Các ca SePay và cash riêng không phụ thuộc các slot này. Có một HTTP 500 cố ý trong fault injection SePay; không phải lỗi của luồng thanh toán bình thường. Lượt trung gian đã phát hiện/fix mapping partial DbContext, status 413 bị handler đổi thành 500 và helper test đọc nhầm envelope của reports; kết quả chỉ tính lượt chạy lại sau sửa.

Build API/runner đạt. Cảnh báo `System.Security.Cryptography.Xml` 9.0.0 (NU1903) của lượt trước đã được xử lý bằng bản 10.0.10 khi chuẩn bị [deploy Render/Azure](deploy-render-azure.md); vulnerability audit hiện không báo package dễ bị tấn công của API (06/10/2026). Cần tiếp tục quét trước các lần deploy, không suy ra hệ thống đã đủ an toàn production chỉ từ dependency audit. Database/process/file kiểm thử đã dọn; migration DB ứng dụng do người dùng thực hiện riêng, không commit/push.

Sau khi dọn tooling kết nối local không còn sử dụng (06/10/2026), build API/runner đạt 0 warning/error; chạy lại SePay trực tiếp đạt **129 HTTP/SQL**, **31 cấu hình/HMAC**, **7 time/slot**, và hosting audit đạt **31**. Runner từ chối tham số không còn hỗ trợ trước khi truy cập database. Các kiểm tra này không gọi SePay thật, không thay đổi FoMedDb/Azure và không thay cho nghiệm thu API trên Render.

Frontend có kiểm thử riêng bằng API/clipboard/QR giả lập trên desktop/mobile, không được coi là nghiệm thu dashboard SePay hoặc thanh toán thực tế. Chưa nghiệm thu dashboard SePay thực tế, Live/ngân hàng thật, hình ảnh QR được ngân hàng đọc, hoàn tiền/đối soát có audit hoặc tải lớn. Write lock toàn workflow hiện tại là bảo vệ correctness, chưa tối ưu thông lượng. HMAC timestamp chống replay request cũ; webhook retry phải được SePay ký timestamp mới. Không nhận Webhook API Key/OAuth/không xác thực trong giai đoạn này.

Nút Gửi thử webhook của SePay có thể dùng ID mẫu 0; backend cố ý từ chối ID không dương để không ghi khoản thu giả. Dùng **Mô phỏng giao dịch** trong Test mode để kiểm thử nghiệp vụ với ID giao dịch dương và mã QR vừa tạo. Không tắt xác thực để làm Gửi thử trả thành công.

## Tài liệu nguồn

- https://developer.sepay.vn/vi/sepay-webhooks/xac-thuc
- https://developer.sepay.vn/vi/sepay-webhooks/tich-hop-webhook
- https://developer.sepay.vn/vi/sepay-webhooks/tao-qr-va-form-thanh-toan
- https://developer.sepay.vn/vi/sepay-webhooks/test-mode/bat-dau-nhanh

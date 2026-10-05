# VC-11 — Thu ngân

## API đã có

- `GET /api/invoices/eligible?page=1`: Receptionist/Admin xem các bệnh án đã hoàn tất, không còn chỉ định chờ và chưa có hóa đơn đang hoạt động.
- `POST /api/invoices`: tạo hóa đơn từ `medicalRecordId`; tổng tiền gồm phí khám `FeeSnapshot` của lịch và các dòng dịch vụ/thuốc đã hoàn tất.
- `GET /api/invoices`, `GET /api/invoices/{id}`: danh sách/chi tiết hóa đơn; bệnh nhân chỉ xem hóa đơn của mình.
- `POST /api/invoices/{id}/payments`: ghi nhận thanh toán một phần hoặc đủ, chặn thanh toán vượt số còn lại.
- `POST /api/invoices/{id}/cancel`: hủy hóa đơn chưa phát sinh thanh toán; thao tác ghi `AuditLog` và không cho hủy hóa đơn đã thu tiền.

`InvoiceResponse` có thêm `consultationFee`, `remainingAmount` và `statusName`. Trước khi chạy API cần thực thi `database/migrations/20260930_add_consultation_fee_to_invoices.sql` trên `FoMedDb`; dữ liệu hóa đơn cũ được gán mặc định 0. BHYT/giảm giá, hoàn tiền, PDF và kết chuyển kế toán chưa được tự suy đoán vì review chưa chốt schema/chính sách tương ứng.

## UI thu ngân sau mục 5

`/reception/cashier` có hai nhóm Hóa đơn đã lập và Chờ lập hóa đơn, phân trang server 20 kết quả. Nhóm chờ dùng API eligible, mở modal đối chiếu phí khám/dịch vụ/thuốc và tổng dự kiến. Xác nhận chỉ gửi `medicalRecordId`; server tính tổng chính thức. Lỗi giữ modal, thành công chuyển tới chi tiết hóa đơn và thông báo đã lập.

**Quyết định UI mới:** chỉ xác nhận thanh toán đủ số dư. Tiền mặt nhập Tiền khách đưa, cho phép đưa đủ/dư và hiển thị Tiền thừa cần trả; nhập thiếu hoặc sai định dạng bị chặn. Không có nút Thu toàn bộ. Thẻ/chuyển khoản/ví xác nhận đúng số dư, không tính tiền thừa. POST luôn gửi số dư, không gửi toàn bộ tiền khách đưa. Quyền hủy khi chưa thu và khóa thao tác đang gửi được giữ nguyên.

API thu một phần vẫn giữ nguyên cho phát triển sau; hóa đơn cũ có khoản thu trước được thanh toán đúng số dư còn lại. Luồng UI mới kiểm tra hóa đơn 420 = phí khám 300 + dịch vụ Completed 100 + thuốc 20, nhận tiền mặt 500, thu vào hóa đơn 420, trả lại 80; không tính chỉ định Canceled hoặc tiền thừa vào doanh thu.

Đã bổ sung `cashReceived`, người thu đã xác thực và snapshot tên theo từng payment; server tính `changeAmount`, trả trong GET/POST hóa đơn. UI thu ngân và hóa đơn bệnh nhân dùng chung lịch sử giao dịch, phục hồi sau reload. Giao dịch cũ giữ NULL, không suy đoán từ người lập hóa đơn. FE gửi `amount` bằng số dư, `cashReceived` riêng cho tiền mặt, không gửi tiền thừa như giá trị đáng tin cậy.

Request mới có `idempotencyKey` UUID. Cùng người thu/key/payload trả giao dịch đã có; khác payload trả 409. FE giữ key cho retry không đổi trong lượt đang mở, refresh hóa đơn để đối soát khi mất response. Client cũ không có key vẫn được hỗ trợ nhưng không có bảo đảm idempotency này.

Trước khi chạy API mới cần execute `database/migrations/20261005_add_payment_cash_audit.sql` trên đúng FoMedDb rồi khởi động lại API. Lượt triển khai chỉ kiểm thử trên database tạm, không tự áp dụng lên dữ liệu ứng dụng. Chi tiết: `docs/payment-cash-audit.md` và `FoMed-Frontend/docs/MONEY-INPUT-AUDIT.md`.

# Nhật ký bệnh án — VC-24

Mục 3 trong backlog sau audit workflow. Đối chiếu review-erd-net06: “ai đã xem hoặc sửa bệnh án nào, lúc nào”. Endpoint thực tế vẫn là `GET /api/audit-logs` (Admin), không dùng đường dẫn mẫu `/api/admin/audit-logs` của review.

## Phạm vi ghi nhận

| Action | Source | Nghiệp vụ |
| --- | --- | --- |
| Create | CreateRecord | Tạo bệnh án, chuyển lượt khám sang InProgress |
| Update | UpdateRecord | Cập nhật bệnh án |
| Finalize | FinalizeRecord | Chốt bệnh án, hoàn tất lượt khám |
| Update | CreatePrescription / UpdatePrescription | Tạo / cập nhật đơn thuốc |
| Update | OrderService / CancelServiceOrder | Thêm / hủy chỉ định |
| Update | SaveLabResult | Kỹ thuật viên lưu kết quả |
| Read | Record / RecordList | Xem chi tiết / danh sách bệnh án |
| Read | Prescription / ServiceOrders / PrescribingContext | Xem đơn, chỉ định/kết quả, thông tin kê đơn |
| Read | DoctorQueueHistory | Lịch sử khám trả trong hàng đợi bác sĩ |
| Read | PatientHistorySummary | Tóm tắt lịch sử trả cho quầy lễ tân/admin |
| Read | LabQueue | Chỉ định trả trong hàng đợi xét nghiệm |
| Read | LabResultHistory | Kết quả trả trong lịch sử của kỹ thuật viên |
| Update | UploadAttachment | Thêm metadata file; nhóm trường Attachments |
| Read | AttachmentList / DownloadAttachment | Metadata/tải file qua quyền sở hữu bệnh án/kết quả |

Ghi đọc theo từng ID bệnh án thực sự được trả về. Trang danh sách/hàng đợi rỗng không tạo log bệnh án; trong một hàng đợi, ID lặp được gộp trong cùng yêu cầu. Không thay đổi phân quyền xem/sửa sẵn có. Đây là phạm vi endpoint được triển khai cụ thể, không phải tuyên bố đã có middleware bao phủ mọi API y tế/file/PDF.

Log Create/Update/Finalize nằm trong **cùng transaction** với thay đổi nghiệp vụ. Không ghi nhật ký thành công cho yêu cầu 403/404/409 hoặc transaction rollback. Lỗi lưu log khiến thao tác ghi thất bại và rollback, kể cả tạo bệnh án cần Save lần đầu để lấy ID. Log đọc lưu sau truy vấn/phân quyền thành công và trước trả response; không khẳng định trình duyệt đã nhận/đọc response nếu kết nối bị ngắt.

## Dữ liệu và quyền truy cập

- Dùng `audit.audit_logs` hiện có; `entity=MedicalRecord`, `entityId` là ID bệnh án. Không cần migration/seed SQL.
- `new_value` lưu JSON metadata phiên bản 1: tên/tài khoản/vai trò người thực hiện **tại thời điểm thao tác**, source, IP kết nối, trace ID server và tên nhóm trường thay đổi. Không lưu giá trị chẩn đoán, triệu chứng, sinh hiệu, ghi chú, dị ứng, đơn thuốc hoặc kết quả xét nghiệm trong log mới này.
- Người thực hiện lấy từ ID JWT đã xác thực và tài khoản/vai trò trong DB. Không nhận actor/IP/trace từ payload hoặc header tự khai.
- IP lấy `RemoteIpAddress`; trace lấy `HttpContext.TraceIdentifier`. Khi triển khai sau reverse proxy cần cấu hình trusted proxy riêng; hiện có thể ghi IP proxy, không mặc định tin `X-Forwarded-For`.
- API chỉ trả metadata có kiểu: `fullName`, `username`, `roles`, `actorSnapshot`, `source`, `ipAddress`, `requestId`, `changedFields`. Với MedicalRecord, `oldValue/newValue` luôn null, kể cả dữ liệu legacy từng chứa thông tin lâm sàng.
- Log cũ không bị sửa/backfill. JSON cũ không hợp lệ hoặc không đúng version được xử lý an toàn; tên/vai trò lấy từ tài khoản hiện tại, `actorSnapshot=false`, source/IP/trace không có thì để null. UI ghi rõ giới hạn này.
- Chỉ Admin được đọc API audit. API không có sửa/xóa audit; đây là append-only ở tầng ứng dụng, **không thay thế** quyền SQL, retention, chống chỉnh sửa bởi DBA hay cơ chế ghi failed-access của hệ thống bảo mật.

## Bộ lọc và giao diện

`GET /api/audit-logs?entity=MedicalRecord&action=Read&userId=&from=&to=&page=1&pageSize=10`.

`/admin/audit-logs` mặc định truy cập (`Read`), có thể chọn tất cả/tạo/cập nhật/chốt; không trộn nhật ký tài khoản, kho hoặc hóa đơn vào trang này. Từ ngày/đến ngày theo UTC+07; `from` bao gồm, `to` không bao gồm. FE chuyển “đến ngày” thành đầu ngày kế tiếp. API chấp nhận thời gian có offset; thời gian không có offset được hiểu là giờ phòng khám. Thời điểm trả về có `Z` rõ ràng.

Bộ lọc có bản nháp và bản áp dụng, chỉ gọi khi Lọc/Xóa lọc; áp dụng về trang 1. Phân trang server 10 bản ghi, thứ tự ID giảm dần. Bảng hiển thị thời gian, người thực hiện/vai trò, loại ghi nhận cụ thể, mã bệnh án, IP và nút Chi tiết. Modal chỉ metadata, có giải thích snapshot/legacy, tên nhóm trường thay đổi, native dialog quản lý focus/Escape; có scroll trong viewport mobile. Có loading, lỗi/thử lại, validation và trạng thái không có dữ liệu.

## Kiểm thử

`tests/ClinicWorkflow/HttpWorkflowAudit.cs` chạy HTTP/JWT trên database ngẫu nhiên, không ghi dữ liệu ứng dụng. Có fault injection trigger chỉ trên DB tạm để kiểm tra rollback Create/Update/Finalize; HTTP 500 ở ba ca này là **cố ý**, không phải lỗi tải màn hình bình thường. Kiểm tra header giả, đổi tên/vai trò sau thao tác, đọc legacy sai JSON/ẩn snapshot, ngày UTC+07, phân trang, quyền Admin và API không sửa/xóa. Browser script có kiểm thử bộ lọc/modal/phân trang/lỗi mạng ở UI thật.

Cách build/chạy trong `docs/clinic-workflow-audit-20261005.md`. Khởi động lại API bằng source mới để thấy metadata/log mới. Nhật ký lịch sử không thể tự có IP hoặc danh tính snapshot chưa từng được ghi nhận.

Lượt nghiệm thu 05/10/2026: **164 assertions HTTP, 152 assertions UI, 51 regression SQL và 7 time/slot checks đạt**, không còn ca bị bỏ để né audit ghi. Runner HTTP báo 167/0 vì cộng 3 kiểm tra browser/tình trạng DB; không cộng lại 152 UI vào con số runner. Regression phân quyền/auth, FE lint/TypeScript/Vite và API build đạt. Còn cảnh báo NU1903 dependency `System.Security.Cryptography.Xml` 9.0.0 và bundle FE lớn, không xử lý trong mục này. API/preview kiểm thử đã dừng, DB tạm đã xóa; không sửa DB ứng dụng hoặc commit/push.

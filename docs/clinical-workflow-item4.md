# Mục 4 — Chọn đơn/preview FEFO, lịch sử kết quả và file đính kèm

Triển khai dựa trên FoMed-API và [review-erd-net06](https://review-erd-net06.vercel.app/), đặc biệt VC-05, VC-14 và VC-16. Không dùng FoMed-FE. URL API bên dưới là hợp đồng thật của FoMed, không phải đường dẫn minh họa trong review.

## Dược sĩ — VC-16

- `/pharmacy/dispense` là danh sách đơn có thuốc, bệnh án đã chốt **và** lượt khám Completed. Lọc tên bệnh nhân/mã BN/mã đơn, trạng thái chưa phát đủ/đã phát đủ; chỉ áp dụng khi bấm Lọc, về trang 1. Phân trang server 10 đơn/trang, ID giảm dần. Vẫn có tra cứu trực tiếp mã đơn.
- `GET /api/pharmacy/prescriptions?keyword=&status=pending&page=1`, status `pending` hoặc `dispensed`. Pharmacist/Admin; page 1–100000, keyword tối đa 100 ký tự. Response wrapper chứa `items/page/pageSize/total`.
- `GET /api/pharmacy/prescriptions/{id}` giữ các trường trạng thái cũ, bổ sung từng dòng: `unit`, `availableQuantity`, `remainingQuantity`, `shortageQuantity`, `proposedBatches` (batchId/lotNumber/expiryDate/availableQuantity/proposedQuantity).
- `/pharmacy/dispense/{id}` hiển thị thuốc và bảng lô đề xuất **trước** khi xác nhận. Một thuốc có thể chia nhiều dòng lô; FEFO theo hạn dùng, sau đó ID lô. Chỉ tính lô còn hạn, tồn dương, thuốc active; hạn đúng ngày hiện tại còn được tính theo quy tắc API hiện có.
- Preview không giữ chỗ/trừ kho. Nút Tải lại tồn kho lấy preview mới. Thiếu bất kỳ thuốc nào thì không cho xác nhận cả đơn; hiển thị số lượng thiếu cụ thể.
- `POST /api/pharmacy/prescriptions/{id}/dispense` vẫn tự chọn lô tại server và kiểm tra lại trong transaction; không tin allocation gửi từ FE. Preview không bảo đảm lô không đổi giữa GET/POST. 409 giữ đơn đang xem, tải lại tồn; thành công hiển thị phân bổ thực tế, khóa phát lại. Retry của đơn đã phát đủ vẫn idempotent.
- Không thêm điều kiện đã thanh toán vào phát thuốc. Giữ nhận diện dữ liệu cấp phát cũ qua BatchId/OUT và khóa sửa đơn đã phát.
- **Chưa có** API báo thiếu thuốc/gửi thông báo bác sĩ của VC-16. Không thêm nút báo thành công giả; đây là backlog riêng, cần thiết kế notification/acknowledgement.

## Kỹ thuật viên — lịch sử trên server

- `GET /api/clinical/lab-results?keyword=&page=1`: Technician active, chỉ kết quả Completed **do tài khoản này trả**, 10/trang, ResultAt giảm dần rồi ID. Tìm theo dịch vụ, tên/mã bệnh nhân; giới hạn bộ lọc như danh sách đơn.
- `/technician/results` lấy trực tiếp server, không dùng sessionStorage làm nguồn nghiệp vụ. Có trạng thái đang tải, rỗng, lỗi/thử lại; bảng bệnh nhân/dịch vụ/kết quả/kết luận/khoảng tham chiếu/thời điểm/trạng thái và nút File kết quả.
- `/technician/orders` vẫn POST `/api/clinical/lab-orders/{id}/result`; lưu thành công loại chỉ định khỏi hàng chờ, kết quả xuất hiện ở lịch sử kể cả phiên đăng nhập khác. Không cho sửa kết quả lần hai.
- Timestamp kết quả mới/đọc lại được trả với `Z` (DB lưu UTC). Log Read nguồn `LabResultHistory` theo ID bệnh án thực sự trả, gộp ID trùng trong cùng response; không ghi nội dung xét nghiệm vào audit.
- Technician không có quyền xem toàn bộ bệnh án hoặc file hồ sơ; chỉ xem/thêm file của kết quả chính mình đã trả. Danh sách kết quả của mọi kỹ thuật viên chưa nằm trong phạm vi trang cá nhân này.

## File đính kèm — VC-14/VC-05

| Chức năng | API | Quyền |
| --- | --- | --- |
| Danh sách metadata, 10/trang | GET `/api/clinical/records/{recordId}/attachments?page=1&orderId=` | Bác sĩ phụ trách; bệnh nhân sở hữu sau chốt + Completed; kỹ thuật viên chỉ orderId có kết quả do mình trả |
| Thêm file multipart trường `file` | POST cùng đường dẫn, orderId tùy chọn | Bác sĩ phụ trách khi InProgress/chưa chốt; Technician chỉ kết quả Completed của mình |
| Tải binary, không trả URL công khai | GET `/api/clinical/attachments/{id}/download` | Kiểm tra sở hữu bệnh án/kết quả như trên |

`orderId` không có: file hồ sơ (`OwnerType=MedicalRecord`, OwnerId=recordId). Có `orderId`: API tìm kết quả của chỉ định và lưu file với `OwnerType=LabResult`, **OwnerId là ID LabResult**; response/UI vẫn dùng `orderId` là ID chỉ định MedicalRecordService. Hai ID không được giả định bằng nhau. Chỉ định phải Completed. Kỹ thuật viên được append file vào kết quả của mình sau khi chốt; không sửa/xóa kết quả hoặc file cũ. Đây là quy tắc bổ sung file kết quả, không mở quyền sửa bệnh án đã chốt cho bác sĩ.

- UI dùng chung `features/clinical/components/AttachmentPanel.tsx`: bác sĩ ở trang chỉ định, kỹ thuật viên mở File kết quả từ lịch sử, bệnh nhân mở Tải file đính kèm trong modal bệnh án. Giữ đúng 3 bảng cũ của modal; panel file là danh sách riêng, có phân trang.
- Chọn file rồi bấm Đính kèm file để upload; không tự gửi khi chọn. Success/error inline, giữ file khi lỗi, chống click trùng trong request, làm mới danh sách sau thành công. Download dùng bearer token và blob, tên file tiếng Việt được giữ theo `filename*` UTF-8.
- JPG/JPEG, PNG, PDF, DICOM/DCM; tối đa **10 MB/file**, không nhận file rỗng. Kiểm tra tên, đuôi và chữ ký file phía server, không tin MIME của client. DICOM chỉ hỗ trợ Part 10 có DICM tại offset 128, không hỗ trợ raw dataset thiếu preamble.
- Đây là kiểm tra chữ ký/định dạng cơ bản, **không phải** parser xác minh toàn bộ tài liệu, antivirus, DICOM viewer hay bộ loại bỏ dữ liệu nhạy cảm trong file. Cần scanner/quarantine, giới hạn tốc độ và quota trước triển khai production quy mô lớn.
- File lưu tên GUID trong kho riêng, mặc định `FoMed-API/FoMed.Api/App_Data/clinical-attachments/`; không phục vụ qua wwwroot/static. Production đặt `ClinicalAttachments__StoragePath` vào private persistent volume, không thư mục web/public. Không đưa file vào git; backup cả DB và kho file, giữ quyền filesystem chặt chẽ.
- Download có Content-Disposition attachment, Cache-Control no-store và nosniff. Danh sách chỉ metadata, không FileUrl/storage key/path. Admin có quyền audit không tự có quyền đọc file lâm sàng; Receptionist/Pharmacist không được đọc file này.
- File cũ chỉ có FileUrl chưa được chuyển đổi được ghi rõ và khóa tải; không tải URL ngoài hoặc đọc đường dẫn tùy ý. Không tự migrate file legacy.
- Metadata + audit Update/UploadAttachment được commit cùng transaction. Lỗi DB/audit dọn file mới đã lưu; kiểm thử rollback có fault injection. Filesystem và SQL **không có distributed transaction**: process crash giữa lưu file/commit có thể để file mồ côi; cần reconciliation và backup vận hành riêng, không có job xóa tự động trong mục này.
- Log Read nguồn AttachmentList/DownloadAttachment không chứa bytes hoặc tên/nội dung y tế của file. Download audit ghi khi server chuẩn bị trả file, không khẳng định client nhận đủ bytes. Không có endpoint sửa/xóa attachment.

## Cài đặt trên database hiện tại

1. Sao lưu, chọn đúng database FoMed trong SQL Server và chạy `database/migrations/20261005_add_attachment_metadata.sql`. Script thêm bốn cột nullable, có thể chạy lại; không reset schema, không seed dữ liệu hay đổi FileUrl cũ.
2. Khởi động lại API bằng source mới. Nếu chưa chạy migration, truy vấn attachments sẽ lỗi do thiếu cột; restart API không tự áp dụng SQL.
3. Chạy frontend mới. FE dùng cùng-origin `/api` qua proxy hiện có; nếu tự đổi thành API cross-origin phải cấu hình CORS/Content-Disposition ở hạ tầng riêng.

Trong đợt triển khai này chỉ áp dụng SQL trên database ngẫu nhiên của kiểm thử, **chưa chạy migration lên database ứng dụng**. Không cần đăng nhập từng account để cập nhật dữ liệu.

## Kiểm thử và giới hạn

Runner `tests/ClinicWorkflow/HttpWorkflowAudit.cs` tạo DB/kho file tạm riêng, áp dụng migration, dùng HTTP/JWT thật, dừng API kiểm thử và xóa đúng DB/kho tạm trong finally. Không làm thay đổi dữ liệu FoMedDb.

Ca mới bao gồm: danh sách/preview FEFO chia lô, thiếu tồn/không trừ tồn khi GET, phân trang đơn/kết quả/file, phạm vi kỹ thuật viên, ownership sau chốt, giả định dạng/tên đường dẫn/quá 10 MB/file rỗng, upload rollback, PDF/PNG/DICOM, binary download và legacy URL không fetch. Browser script mở 23 route ở 1440/390px, thực hiện upload/download thật, trả kết quả/lịch sử, chọn đơn/preview/phát thuốc và các regression mục 1–3.

Chưa thay thế load test, nghiệm thu DICOM viewer/parser/antivirus, migration file cũ, quota, backup/recovery hoặc full UI click-through toàn bộ phòng khám. PDF đơn thuốc/hóa đơn, đối chiếu dị ứng theo hoạt chất, báo thiếu gửi bác sĩ và phân trang danh mục dịch vụ vẫn ở backlog.

Kết quả 05/10/2026: **226 HTTP và 178 UI đạt**, runner 229/0 (3 kiểm tra tổng hợp/browser/DB), SQL 51/time-slot 7 và regression auth/phân quyền đạt. Bao gồm fixture orderId khác LabResult.Id, kiểm tra OwnerId lưu đúng và upload/list/download qua UI. FE lint/TypeScript/Vite, API build đạt; còn cảnh báo dependency NU1903 và bundle lớn từ trước. Bốn HTTP 500 fault injection là cố ý; tải trang UI bình thường không có JS error/API 5xx/document overflow. DB/kho file tạm đã xóa; API/preview kiểm thử đã dừng. Không chạy migration trên DB ứng dụng hoặc tạo commit/push.

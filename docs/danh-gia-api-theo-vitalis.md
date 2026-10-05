# Đánh giá FoMed-API theo đặc tả Vitalis

Ngày rà soát: 2026-09-30.

## 1. Phạm vi và cách đánh giá

- Nguồn yêu cầu: [Review ERD .NET06 — Minh / Vitalis](https://review-erd-net06.vercel.app/), 24 màn VC-01–VC-24, API/DTO, checklist và phần nhận xét ERD. Nội dung đã đọc trong phiên làm việc này.
- Nguồn thực hiện: controller, DTO, service, repository, model, cấu hình EF, script tạo database và hai chương trình test trong workspace hiện tại.
- Source đang có thay đổi chưa commit, gồm `staff-appointments`, quyền lịch hẹn và các file khác. Đánh giá này tính cả source đó; không khẳng định server đang chạy đã nạp cùng bản build.
- Sau lượt đánh giá, bước hoàn tất lịch đã được siết điều kiện trong API và bổ sung kiểm thử hồi quy. Chưa sửa schema/database.
- Schema được đối chiếu từ script và mapping, chưa kiểm tra schema/data của SQL Server đang chạy.
- Tên route khác đặc tả là khác hợp đồng tích hợp, không tự động là sai nghiệp vụ. Bảng dưới ưu tiên quyền, dữ liệu và điều kiện chuyển trạng thái; đổi URL đơn thuần không giải quyết các khoảng thiếu.
- Điểm ERD trên website đánh giá một bản thiết kế khác thời điểm; không dùng điểm đó làm tỷ lệ hoàn thành FoMed.

## 2. Kết luận

FoMed có nền tảng tái sử dụng được, nhưng hiện là một luồng khám rút gọn. Chưa đủ cơ sở nghiệm thu theo đầy đủ đặc tả Vitalis. Cần chỉnh vòng đời nghiệp vụ trước khi mở rộng CMS.

Theo cách phân nhóm ở mục 4: **16 chức năng có một phần nền tảng/API, 8 chức năng chưa có API thực hiện cho đúng vai trò**. Chưa xác nhận chức năng nào đạt toàn bộ chi tiết đặc tả. Đây không phải tỷ lệ hoàn thành hay kết luận rằng mọi endpoint đều lỗi.

Những phần nên giữ:

- Ba project Api/Application/Infrastructure; controller gọi service, trả DTO thay vì entity.
- BCrypt, kiểm tra JWT và các kiểm tra quyền sở hữu dữ liệu trong service.
- Đặt lịch kiểm tra slot, thời gian, nghỉ cá nhân/toàn phòng khám, chồng lấn bác sĩ và bệnh nhân.
- Sequence sinh mã bệnh nhân, lịch hẹn, hóa đơn; lịch sử chuyển trạng thái.
- Transaction và khóa SQL qua `WriteScope` cho các thao tác khám/đặt lịch/thanh toán.
- Tách bệnh án, đơn thuốc, chỉ định, kết quả, hóa đơn và thanh toán.
- Hóa đơn lưu dòng mô tả/đơn giá; server tính tiền; có chặn thanh toán vượt số còn lại.
- Script có filtered unique cho bệnh nhân có user, CHECK và FK ở nhiều bảng.

Không cần tái cấu trúc toàn bộ tầng kỹ thuật để sửa các luồng này. Application đang tham chiếu Infrastructure và dùng EF thông qua `ClinicRepository`; đây là phân lớp hiện hữu, không phải mô hình Clean Architecture độc lập hoàn toàn. Việc đổi kiến trúc không phải ưu tiên của đợt này.

## 3. Các phát hiện cần xử lý trước

### F01 — Hoàn tất lịch chưa tương đương chốt bệnh án (ưu tiên cao)

**Bằng chứng ban đầu:** `AppointmentService.CompleteAppointmentAsync` cho phép cả Confirmed và InProgress chuyển Completed, không kiểm tra bệnh án, chẩn đoán hoặc chỉ định chưa xong. `ClinicalService.CreateRecordAsync` chỉ tạo bệnh án từ Confirmed; `GetEditableAsync` chỉ sửa khi InProgress.

**Cập nhật sau rà soát:** API hiện chỉ cho chuyển `InProgress -> Completed`; cần có bệnh án, chẩn đoán không rỗng và không còn chỉ định dịch vụ ở trạng thái chờ (`Status == 0`). API trả 409 nếu thiếu điều kiện. Các kiểm tra này dùng quan hệ và trạng thái đã có, không cần migration. Bộ test phân quyền có thêm ca cho trạng thái sai, thiếu bệnh án, thiếu chẩn đoán, chỉ định đang chờ và bệnh án hợp lệ.

**Hệ quả:** lịch đã xác nhận nhưng chưa khám có thể chuyển Completed; sau đó không tạo bệnh án qua luồng hiện tại được. Một ca đang khám cũng có thể hoàn tất khi chưa đủ dữ liệu; việc sửa bệnh án bị chặn ngay sau đó. `BillingService.CreateAsync` mới kiểm tra chỉ định chờ, nên điều kiện chốt và điều kiện lập hóa đơn không cùng một bước.

**Phần còn thiếu:** hiện đây mới là kiểm tra điều kiện trước khi hoàn tất lịch; chưa có trạng thái `Finalized` riêng, chưa kết chuyển hóa đơn trong cùng transaction và quyền đọc bệnh án nháp vẫn cần xử lý theo F04. Cần thiết kế bước chốt nguyên tử trước khi tuyên bố hoàn tất toàn bộ vòng đời khám.

**Kiểm thử cần có:** không hoàn tất trước khám; không chốt thiếu chẩn đoán/còn chỉ định chờ; rollback nếu tạo hóa đơn lỗi; chốt hai lần không sinh hai hóa đơn; bệnh án đã chốt không bị sửa.

### F02 — Thiếu bước tiếp nhận và vòng đời hàng chờ

**Bằng chứng ban đầu:** `BookAppointmentAsync` gán QueueNumber ngay khi đặt; chưa có API check-in cập nhật CheckedInAt hay endpoint hàng chờ đã đến.

**Cập nhật sau bước này:** lịch mới chưa được cấp số lúc đặt. Lễ tân/Admin có thể gọi `PUT /api/appointments/{id}/check-in` cho lịch Confirmed trong ngày; API ghi `CheckedInAt`, cấp số theo hàng chờ đã check-in và ghi sự kiện vào lịch sử. `GET /api/appointments/waiting-queue?date=YYYY-MM-DD` trả danh sách đã check-in trong ngày. Bác sĩ chỉ có thể bắt đầu tạo bệnh án sau check-in. Dùng cột hiện có, không đổi enum `AppointmentStatus` hoặc schema.

**Giới hạn còn lại:** chưa có gọi lại, chuyển cuối hàng, no-show từ quầy, phân trang/thống kê hàng chờ hay cập nhật thời gian thực. Check-in chỉ chấp nhận ngày hẹn theo ngày địa phương của phòng khám; chưa áp dụng giờ mở quầy.

**Quy ước hiện tại:** biểu diễn đã đến bằng `CheckedInAt`, giữ nguyên ý nghĩa enum đang lưu, cấp QueueNumber lúc check-in và lọc hàng chờ theo ngày. Cần kiểm thử với SQL Server đang dùng trước khi xác nhận tính nguyên tử khi hai quầy check-in đồng thời. Không tự chèn enum rồi đổi nghĩa các số đang lưu.

**Kiểm thử:** hai quầy check-in đồng thời không trùng số; check-in lại không cấp số lần hai; chưa đến không vào hàng chờ; bác sĩ không thao tác hàng chờ người khác.

### F03 — Đăng ký và đặt lịch chưa phục vụ bệnh nhân vãng lai

**Bằng chứng trước khi bổ sung:** `RegisterAsync` luôn tạo Patient mới; `BookAppointmentAsync` chỉ nhận bệnh nhân từ currentUserId và Source luôn 0.

**Cập nhật VC-08/09:** đã có nhóm `/api/patients/staff` để tìm/tạo/sửa hồ sơ vãng lai và `POST /api/appointments/staff-book` để đặt hộ có kiểm tra quyền, source và created_by. Các trường quầy dùng migration riêng.

**Hướng mở rộng:** phân trang/count chuẩn cho hồ sơ, xác minh CCCD/BHYT theo quy trình phòng khám và đặt hộ kèm thông báo. Không tự liên kết chỉ vì một SĐT người dùng nhập trùng.

**Kiểm thử:** nhiều bệnh nhân user_id NULL; bệnh nhân cũ giữ nguyên ID/lịch sử; không chiếm hồ sơ người khác; lễ tân tạo lịch ghi đúng người tạo; patientId tùy ý không cho bệnh nhân đặt thay người khác.

### F04 — Quyền xem bệnh án chưa phân biệt bản đang soạn và bản đã chốt

**Bằng chứng:** `ClinicalService.ListAsync`, `GetReadableAsync` và `ClinicAccess.CanReadAsync` kiểm tra sở hữu/bác sĩ phụ trách nhưng không kiểm tra Finalized. Model MedicalRecord chưa có trạng thái chốt. Đọc đơn thuốc/chỉ định cũng dùng quyền đọc này.

**Hệ quả:** bệnh nhân có thể đọc dữ liệu đang được bác sĩ hoàn thiện. Đây là khác biệt với điều kiện chỉ công bố bệnh án đã chốt trong đặc tả, không phải bằng chứng truy cập được bệnh án của người khác.

**Hướng sửa:** tách quyền đọc bản đang soạn và bản đã công bố; thống nhất quyền đọc lịch sử của bệnh nhân cho bác sĩ đang điều trị và lễ tân. Hiện bác sĩ chỉ đọc bệnh án do chính bác sĩ đó phụ trách; lễ tân không đọc được bệnh án qua ClinicalAccess.

**Kiểm thử:** bệnh nhân không đọc bản nháp qua list/detail/prescription/services; bác sĩ được giao ca đọc/sửa đúng phạm vi; kiểm tra audit ở mỗi đường đọc.

### F05 — Thời điểm chốt giá, trạng thái hóa đơn và hoàn tiền chưa đúng đặc tả

**Bằng chứng:** BillingService lấy `Service.Price`/`Medicine.Price` tại lúc tạo hóa đơn. Chỉ định và dòng đơn chưa có snapshot giá; tên thuốc response đọc từ danh mục hiện tại. Hóa đơn mới lưu snapshot từ thời điểm lập. FeeSnapshot của lịch chưa có; phí khám và serviceId chọn khi đặt lịch không tự trở thành dòng hóa đơn.

**Hệ quả:** đổi giá sau lúc chỉ định/kê đơn nhưng trước lúc lập hóa đơn có thể đổi tiền; đổi tên danh mục có thể đổi tên hiển thị trên đơn cũ. Hóa đơn chỉ có thuốc/chỉ định đã hoàn thành, có thể thiếu phí khám. Ca không có hai loại dòng này bị từ chối lập hóa đơn.

**Trạng thái hiện tại:** BillingService dùng 0 cho còn nợ, 1 cho đã trả đủ, 2 cho đã hủy. Trả một phần vẫn là 0. Đặc tả dùng trạng thái trả một phần riêng; không được áp số của tài liệu trực tiếp lên dữ liệu cũ.

**Các khoảng thiếu:** BHYT/giảm giá, người nhận tiền, mã giao dịch, PDF, hoàn tiền, hủy hóa đơn và danh sách ca đủ điều kiện lập hóa đơn cho lễ tân. `GET records` không cung cấp danh sách bệnh án cho lễ tân; request tạo hóa đơn lại cần medicalRecordId.

**Ràng buộc schema:** payments.amount hiện CHECK > 0 và DTO chỉ nhận số dương. Hoàn tiền bằng bút toán âm cần sửa cả schema, validation, quyền và cách tính số còn lại; không chỉ thêm endpoint.

**Kiểm thử:** đổi giá không làm đổi chứng từ đã chốt; không tính phí khám hai lần; trả một phần/đủ/vượt; thu đồng thời; hoàn tiền không sửa dòng thu cũ; không hủy hóa đơn đã thu tiền theo chính sách đã chốt.

### F06 — JWT, DTO và quản lý phiên chưa đạt đặc tả

**Bằng chứng:** AuthResponse, JwtTokenService và ProfileService lấy role đầu tiên. LoginRequest nhận email, không nhận username. Tài khoản inactive trả 401. Chưa có refresh/logout/forgot-password và chưa có phát hành/hash/lưu refresh token dù có model/bảng. Đổi mật khẩu chỉ cập nhật hash, không có cơ chế thu hồi phiên.

**Hướng sửa:** chốt định danh đăng nhập, DTO, nhiều role, thời hạn/rotation/thu hồi phiên và khôi phục mật khẩu. Cần phối hợp FE khi đổi payload/response. Không coi việc có bảng refresh_tokens là đã có quản lý phiên.

**Kiểm thử:** đăng nhập nhiều role; email/tên đăng nhập theo hợp đồng; tài khoản khóa; hết hạn/refresh/reuse token; đăng xuất; mật khẩu mới/cũ; khôi phục hết hạn/dùng lại.

### F07 — Kê đơn mới kiểm tra thuốc hoạt động, chưa có nghiệp vụ dược

**Bằng chứng:** ClinicalService kiểm tra số lượng, thuốc lặp, thuốc active, xác nhận dị ứng và lưu giá snapshot; PharmacyService phân bổ FEFO, ghi phát thuốc/tồn kho, nhập lô và điều chỉnh có audit.

**Hướng sửa:** giữ một bệnh án–một đơn theo phương án đề xuất cho đồ án; cho sửa khi còn nháp; lưu snapshot; bổ sung dị ứng/sinh hiệu theo phạm vi. Phát thuốc phải ghi từng lần phân bổ lô và bút toán: một PrescriptionItem.BatchId không biểu diễn đủ việc một dòng thuốc được phát từ nhiều lô.

**Kiểm thử:** thuốc hết hạn/hết tồn; xác nhận cảnh báo dị ứng; một dòng lấy nhiều lô theo FEFO; phát đồng thời không âm kho; gửi lại request không phát hai lần; lịch sử kho đối soát được.

### F08 — Lịch làm việc và quyền quản trị còn khác phạm vi

**Bằng chứng:** DoctorScheduleService quản lý lịch của bác sĩ đăng nhập, không có effectiveFrom/effectiveTo hoặc quản lý nghỉ. Update/Delete chưa kiểm tra tác động đến lịch hẹn tương lai. DoctorService cho bác sĩ tự sửa cả chuyên khoa, phòng và phí khám; đặc tả đặt các trường này trong nhóm quản trị.

**Hướng sửa:** chốt quyền cập nhật các trường nghề nghiệp, phân lịch admin và ngày hiệu lực. Nghỉ trưa có thể biểu diễn bằng nhiều ca nhưng không thay thế yêu cầu hiệu lực theo giai đoạn. Giữ kiểm tra nghỉ toàn phòng khám đã có trong AppointmentRepository.

**Kiểm thử:** đổi lịch từ tháng sau không làm thay đổi diễn giải lịch cũ; sửa ca/nghỉ phép có lịch đã đặt phải trả thông tin xử lý; kiểm tra role của người sửa phí khám/chuyên khoa.

## 4. Đối chiếu đủ 24 chức năng

Quy ước: **Một phần** = có code/API/nền dữ liệu liên quan, còn thiếu hành động hoặc điều kiện. **Chưa có API** = chưa có luồng API cho chức năng và vai trò đó; không phủ nhận có model/bảng.

| Mã | Chức năng | Hiện có ở API | Thiếu/khác cần xử lý | Kết luận |
|---|---|---|---|---|
| VC-01 | Đăng nhập | `POST /api/auth/login` nhận username; BCrypt; JWT đủ roles; `LoginResponse` có accessToken, refreshToken, expiresIn và user (id, fullName, roles, doctorId, patientId); token refresh dùng một lần; inactive trả 403 | `email` còn được nhận như alias request cũ; quên mật khẩu (F06); kiểm thử HTTP/JWT với SQL đang chạy | Một phần |
| VC-02 | Đăng ký | POST auth/register, user + Patient role + hồ sơ/mã | phone/dateOfBirth; email tùy chọn; lookup và liên kết hồ sơ cũ; AuthResponse (F03/F06) | Một phần |
| VC-03 | Đặt lịch | Public doctors/services/specialties; available-slots có kiểm tra serviceId; POST appointments/book; chống trùng/nghỉ; serviceId; feeSnapshot/source trong response | Hiệu lực ca; nhắc lịch; trả slot thay thế khi 409; hợp đồng patientId/source đầy đủ cho đặt hộ và dữ liệu BHYT | Một phần |
| VC-04 | Lịch hẹn của tôi | my-appointments, detail/history, reschedule, cancel | Đã có đổi giờ, lý do hủy bắt buộc, chính sách hủy sát giờ và chặn hủy InProgress; phần đọc bệnh án đã chốt thuộc VC-05 | Một phần |
| VC-05 | Lịch sử khám & đơn | clinical/records, prescription, services theo quyền; bệnh nhân chỉ đọc hồ sơ đã Completed | Bản chốt; audit đọc; file tải có kiểm soát; snapshot đơn/kết quả đầy đủ | Một phần |
| VC-06 | Hóa đơn của tôi | GET invoices/detail giới hạn bệnh nhân; remainingAmount/statusName; thanh toán một phần | BHYT/giảm giá; PDF; hoàn tiền | Một phần |
| VC-07 | Bàn tiếp đón | staff-appointments; lọc doctorId; confirm/cancel/check-in/no-show; hàng chờ | phân trang/count; dữ liệu hồ sơ đầy đủ; tạo bệnh nhân/đặt hộ | Một phần |
| VC-08 | Hồ sơ bệnh nhân tại quầy | Staff search/get/create/update/history; CCCD/BHYT/liên hệ khẩn/dị ứng; audit ghi thay đổi | Phân quyền chi tiết theo chi nhánh; audit đọc; phân trang/count nâng cao | Một phần |
| VC-09 | Đặt hộ/vãng lai | Staff-book chọn patientId; source Phone/WalkIn; created_by; kiểm tra lịch và xung đột | Đặt hộ kèm thông báo; phân trang danh sách hồ sơ; chính sách sửa lịch tại quầy | Một phần |
| VC-10 | H?ng ch?/c?p s? | Check-in c?p QueueNumber; waiting-queue; call-next; move-to-end; no-show theo quy?n | realtime/WebSocket; ph?n trang/count; ki?m th? SQL ??ng th?i | M?t ph?n |
| VC-11 | Thu ng?n | eligible; t?o h?a ??n c? ph? kh?m FeeSnapshot; thanh to?n m?t ph?n/??; ch?n v??t; h?y h?a ??n ch?a thu | BHYT/gi?m gi?; refund; PDF; k?t chuy?n k? to?n; ki?m th? SQL/HTTP | M?t ph?n |
| VC-12 | H?ng ch? b?nh nh?n (b?c s?) | doctor-queue t? kh?a theo b?c s? t? token; tr? d? ?ng v? t?i ?a 5 b?nh ?n g?n nh?t; t?o b?nh ?n v?n b?t bu?c check-in | sinh hi?u c? c?u tr?c; quy?n l?ch s? n?ng cao theo ph?n c?ng | M?t ph?n |
| VC-13 | M?n h?nh kh?m | Sinh hi?u c? c?u tr?c; ICD-10; h??ng ?i?u tr?/t?i kh?m; finalize b?nh ?n khi ho?n t?t l?ch; ch?n s?a sau finalize | Sinh hi?u n?ng cao/ICD master; finalize h?a ??n nguy?n t? | M?t ph?n |
| VC-14 | Ch? ??nh/k?t qu? | quantity + unitPriceSnapshot; Technician nh?p referenceRange; h?y ch? ??nh ch?; h?a ??n d?ng snapshot | kho?ng tham chi?u theo lo?i x?t nghi?m; file ??nh k?m; workflow ?ang th?c hi?n | M?t ph?n |
| VC-15 | Kê đơn | Tạo/đọc/sửa đơn nháp, kiểm tra thuốc active, dị ứng và giá snapshot | Số ngày dùng; PDF (F07) | Đã đáp ứng phạm vi API |
| VC-16 | Phát thuốc | Quyền dược; FEFO đa lô; idempotent; báo thiếu; ghi phân bổ và xuất kho | Trạng thái giao nhận riêng; xác nhận người nhận | Đã đáp ứng phạm vi API |
| VC-17 | Kho thuốc | Danh sách tồn/cận hạn; nhập lô; điều chỉnh có lý do/audit; giao dịch thẻ kho | MinStock theo danh mục | Đã đáp ứng phạm vi API |
| VC-18 | Nhập thuốc | Phiếu nhập nhiều dòng; giá nhập; nhà cung cấp/chứng từ; cập nhật tồn nguyên tử | Giá nhập theo nhà cung cấp nâng cao; trả hàng | Đã đáp ứng phạm vi API |
| VC-19 | Bác sĩ/chuyên khoa admin | Admin tạo/sửa bác sĩ + user + role; CRUD chuyên khoa; chặn ngừng khi còn lịch tương lai | Audit chi tiết thay đổi trường; phân lịch theo chi nhánh | Đã đáp ứng phạm vi API |
| VC-20 | Lịch làm việc/nghỉ | Doctor CRUD ca; Doctor/Admin CRUD nghỉ cá nhân/toàn phòng khám; chặn xung đột lịch | Hiệu lực theo giai đoạn; tự động chuyển lịch bị ảnh hưởng | Đã đáp ứng phạm vi API |
| VC-21 | Danh mục dịch vụ admin | GET/POST/PUT `/api/admin/services`; code/specialty/duration; audit | Bảo vệ dữ liệu đã tham chiếu, không xóa vật lý | Đã đáp ứng phạm vi API |
| VC-22 | Người dùng/phân quyền | Admin list/lọc user, list role, gán nhiều role, reset/khóa, thu hồi phiên, audit | Permission chi tiết theo từng màn hình là phạm vi mở rộng | Đã đáp ứng phạm vi API |
| VC-23 | Báo cáo | Tổng hợp tiền lập hóa đơn/đã thu/công nợ/lượt khám/no-show; lọc kỳ/bác sĩ; export CSV | Excel/PDF nâng cao là phạm vi mở rộng | Đã đáp ứng phạm vi API |
| VC-24 | Nhật ký bệnh án | Admin truy vấn audit log theo entity/action/user/kỳ, phân trang; endpoint chỉ đọc | Ghi audit cho mọi thao tác đọc bệnh án chi tiết cần bổ sung theo từng màn hình FE | Đã đáp ứng phạm vi API |

## 5. Luồng hiện tại và luồng cần chốt

Hiện tại trong code:

```text
Tài khoản có Patient -> Book/Pending + số thứ tự
  -> Confirmed -> tạo bệnh án/InProgress
  -> cần chẩn đoán + hoàn tất/hủy mọi chỉ định đang chờ
  -> Completed -> lễ tân tạo hóa đơn riêng -> thu tiền
```

API từ chối đường `Confirmed -> Completed` và từ chối hoàn tất nếu chưa có bệnh án hợp lệ. Việc tạo hóa đơn vẫn là thao tác riêng, không nguyên tử với hoàn tất lịch.

Luồng đề xuất để khớp nghiệp vụ:

```text
Tự đặt hoặc lễ tân đặt hộ hồ sơ vãng lai
  -> Xác nhận lịch -> Check-in/cấp số -> Hàng chờ
  -> Bắt đầu khám/bệnh án nháp
  -> Sinh hiệu, chẩn đoán, chỉ định và kết quả, đơn thuốc
  -> Chốt bệnh án/khóa sửa/hoàn tất lịch/kết chuyển hóa đơn
  -> Thu ngân và phát thuốc theo chính sách được chốt
```

Đặc tả không đủ rõ để tự suy ra phải thanh toán xong mới phát thuốc hay được phát trước. Cần ghi chính sách này trước khi triển khai VC-16. Không tự đồng nhất hai trạng thái thanh toán và cấp phát.

## 6. Những điểm phải thống nhất trong chính đặc tả

- Màn hình dùng CheckedIn nhưng phần mô tả trạng thái lịch gốc vẫn là 0–5. Cần chốt bảng chuyển trạng thái và mapping số cũ; tránh thay đổi làm hỏng unique filtered index/status history.
- Trang đăng nhập nói 4 vai trò, nhưng module có Pharmacist và nghiệp vụ nhập kết quả cần Technician; bảng ví dụ còn nhắc Cashier. Chốt danh sách role chính thức, quyền nhiều role và quyền đọc dữ liệu. Seed hiện tại có Technician, chưa có Pharmacist; enum UserRole trong source vẫn chỉ có bốn role, cần thống nhất khi sử dụng.
- Đăng ký email tùy chọn nhưng login yêu cầu username: cần quy tắc định danh, không để người đăng ký không biết tên đăng nhập của mình.
- Mẫu hóa đơn có giảm giá, nhưng công thức còn lại trong một số ghi chú chỉ trừ BHYT và tiền đã thu. Chốt totalAmount là trước/sau giảm giá, cách hoàn tiền, làm tròn và trạng thái trước khi viết code.
- Bản nhận xét cuối khuyến nghị một bệnh án–một đơn; sửa đơn khi Draft, sau Finalized tạo lượt khám mới. Không triển khai đơn bổ sung nếu chưa thay thiết kế.
- Nội dung website có phần bắt buộc, phần khuyến nghị và P2. Phân quyền chi tiết permission/role_permission là khuyến nghị trong bản nhận xét; không tự coi mọi đề xuất là điều kiện chặn MVP. Một số tính năng P2 vẫn xuất hiện trên màn hình, nên cần ghi rõ phạm vi nghiệm thu.
- Route/method giữa bảng API tổng hợp và màn hình có khác nhau (ví dụ history/records, reports). Chốt một hợp đồng OpenAPI để FE dùng, không sao chép hai biến thể song song.

## 7. Phạm vi sửa dự kiến và thứ tự thực hiện

| Bước | Kết quả cần đạt | File/nhóm file có thể bị tác động | Ảnh hưởng dữ liệu/FE |
|---|---|---|---|
| 0 | Chốt trạng thái, quyền, DTO, phí khám/snapshot và tiêu chí nghiệm thu | Tài liệu workflow, bảng quyền, hợp đồng request/response | Chưa sửa runtime; lập mapping dữ liệu cũ |
| 1 | Xác thực và hồ sơ bệnh nhân đúng hợp đồng | AuthModels/AuthService/AuthController, JwtTokenService/ITokenService, ProfileService, UserRepository/PatientRepository; module phiên/khôi phục và patient staff | FE auth/guard phải đổi đồng bộ; migration theo thiết kế; không mất user/patient cũ |
| 2 | Lễ tân và hàng chờ hoạt động xuyên suốt | AppointmentController/Service/Repository/Models, AppointmentStatus, PatientController hoặc controller staff mới; DbContext và migration | QueueNumber/CheckedInAt/status phải có kế hoạch chuyển dữ liệu, không cấp lại số lịch cũ tùy tiện |
| 3 | Khám, đơn và chốt bệnh án | ClinicalService/ClinicAccess/ClinicalModels, ClinicalController; MedicalRecord/Prescription/MRS, vitals/allergies; BillingService | Phân biệt hồ sơ cũ/chưa chốt; dữ liệu snapshot quá khứ không thể khôi phục chính xác nếu đã đổi danh mục |
| 4 | Thu ngân hoàn chỉnh | InvoiceController/BillingService/BillingModels, Invoice/Payment/InvoiceItem, ràng buộc SQL | Mapping enum cũ, BHYT/giảm giá, hoàn tiền, chống lặp; FE lịch sử tiền và trạng thái |
| 5 | Dược | Module inventory/receipt/dispense mới, lô/giao dịch và phân bổ dòng thuốc | Migration dữ liệu lô; quyền Pharmacist; kiểm thử tồn đồng thời |
| 6 | Quản trị và báo cáo | Doctor/schedule/service/user admin endpoints, báo cáo, export | Bảo vệ lịch/chứng từ đang tham chiếu trước khi ngừng danh mục |

Audit phải được đưa vào các thao tác đọc/ghi khi hoàn thiện từng module; không để đến bước cuối mới tìm cách ghi lại lịch sử đã mất. Lịch làm việc có hiệu lực là phụ thuộc của bước 2, cần xử lý phần liên quan trước dù giao diện admin đầy đủ ở bước 6.

Không cần đổi toàn bộ đường dẫn cũ ngay. Nếu thay hợp đồng đang có người dùng, có thể chuyển FE theo từng nhóm, giữ alias thích hợp nhưng phải gọi cùng nghiệp vụ và cùng quyền.

## 8. Kiểm tra đã thực hiện trong lượt đánh giá

Build thường bị lỗi sao chép DLL do server hiện tại giữ file. Không dừng server. Build lại với thư mục output riêng thành công:

```powershell
dotnet build FoMed-API/FoMed.sln --no-restore --nologo -p:BaseOutputPath=C:/Users/M/Desktop/FoMed/.local-backups/api-audit-build/ -v:quiet
dotnet run --project tests/AppointmentAuthorization --no-restore -p:BaseOutputPath=C:/Users/M/Desktop/FoMed/.local-backups/api-audit-build/ --verbosity quiet
dotnet run --project tests/ClinicWorkflow --no-restore -p:BaseOutputPath=C:/Users/M/Desktop/FoMed/.local-backups/api-audit-build/ --verbosity quiet
```

Kết quả:

- Build: thành công, 0 lỗi; 8 cảnh báo NU1903 từ metadata NuGet hiện có cho `System.Security.Cryptography.Xml` 9.0.0. Cần rà dependency và advisory trước khi chọn bản cập nhật; lượt này không thay package và không xác minh khả năng khai thác.
- AppointmentAuthorization: 55 ca phân quyền/trạng thái dịch vụ và 6 kiểm tra role attribute của controller PASS; 4 ca login, kiểm tra JWT đa role, 4 ca refresh-token, 4 ca đăng ký và 6 ca đổi mật khẩu PASS. Các ca dịch vụ dùng stub; không thay thế kiểm thử HTTP/SQL.
- FoMed-FE: `tsc -b` và `npx vite build --configLoader runner` PASS sau khi form đăng nhập chuyển sang username/email và client đọc `LoginResponse.user`. Build có cảnh báo chunk JavaScript lớn; chưa kiểm thử bằng trình duyệt.
- ClinicWorkflow không có `--sql`: 7 kiểm tra giờ/slot PASS. Các bước check-in trong workflow SQL chưa chạy.
- Lệnh `ClinicWorkflow --sql` đã tới SQL Server nhưng dừng ở login/TLS với lỗi “requires encryption but this machine does not support it”; chưa tạo database test hoặc chạy các ca tích hợp. Development connection đã đặt `TrustServerCertificate=True`, nên cần kiểm tra phiên bản SQL Server và TLS 1.2/cipher compatibility. Không tạo hay sửa dữ liệu FoMedDb.
- Chưa chạy HTTP/JWT end-to-end; SQL/TLS hiện chặn kiểm thử login thật với database.

Giới hạn: test authorization dùng stub repository và reflection controller; test đăng ký stub token, không chứng minh JWT nhiều role/refresh. Bảy kiểm tra ClinicWorkflow chỉ là helper giờ/slot, không phải toàn luồng khám. Nhánh `--sql` có test tích hợp riêng nhưng chưa chạy lần này.

Các PASS xác nhận hành vi đang được test, không xác nhận đạt toàn bộ đặc tả. Check-in hiện được kiểm tra ở mức logic dịch vụ/stub; cần chạy SQL integration và HTTP/JWT end-to-end khi SQL/API sẵn sàng. Cảnh báo NU1903 hiện có chưa được xử lý trong bước này.

## 9. Điểm đọc source để kiểm chứng

- [AppointmentService](../FoMed-API/FoMed.Application/Services/Appointment/AppointmentService.cs): BookAppointmentAsync, CompleteAppointmentAsync, CancelAppointmentAsync, GetStaffAppointmentsAsync.
- [AppointmentRepository](../FoMed-API/FoMed.Infrastructure/Repositories/AppointmentRepository.cs): đọc lịch, overlap, time-off, số thứ tự.
- [ClinicalService](../FoMed-API/FoMed.Application/Services/Clinical/ClinicalService.cs): tạo/sửa/đọc bệnh án, kê đơn, chỉ định/kết quả.
- [ClinicAccess](../FoMed-API/FoMed.Application/Services/Clinical/ClinicAccess.cs): quyền đọc và bác sĩ phụ trách.
- [BillingService](../FoMed-API/FoMed.Application/Services/Billing/BillingService.cs): điều kiện tạo hóa đơn, giá và thanh toán.
- [AuthService](../FoMed-API/FoMed.Application/Services/Auth/AuthService.cs), [AuthModels](../FoMed-API/FoMed.Application/DTO/Auth/AuthModels.cs), [JwtTokenService](../FoMed-API/FoMed.Infrastructure/Authentication/JwtTokenService.cs).
- [DoctorScheduleService](../FoMed-API/FoMed.Application/Services/Doctor/DoctorScheduleService.cs), [DoctorService](../FoMed-API/FoMed.Application/Services/Doctor/DoctorService.cs).
- [WriteScope](../FoMed-API/FoMed.Infrastructure/UnitOfWork/WriteScope.cs): khóa SQL chung cho các thao tác ghi; có thể giới hạn thông lượng, chưa có đo tải để kết luận hiệu năng.
- [Schema SQL](../database/fomed-create-database.sql), [DbContext](../FoMed-API/FoMed.Infrastructure/DbContext/FoMedDbContext.cs).
- [Test phân quyền/đăng ký/mật khẩu](../tests/AppointmentAuthorization/Program.cs), [test workflow](../tests/ClinicWorkflow/Program.cs).

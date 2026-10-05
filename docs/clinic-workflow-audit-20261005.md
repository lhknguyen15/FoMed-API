# Kiểm tra xuyên suốt FoMed — 05/10/2026

## Cập nhật mới nhất — giao dịch tiền mặt lưu tại backend

Backend đã lưu tiền khách đưa, người thu xác thực/snapshot tên và UUID chống ghi nhận lặp; tiền thừa tính từ payment, không tăng doanh thu. Thu ngân và hóa đơn bệnh nhân dùng chung lịch sử API, phục hồi sau reload; dữ liệu cũ giữ NULL. Cùng người thu/key/payload được replay không thu lại, khác payload trả 409. API thu một phần vẫn giữ nguyên. Chi tiết hợp đồng tại `docs/payment-cash-audit.md`.

Trước chạy API mới cần execute `database/migrations/20261005_add_payment_cash_audit.sql` trên đúng FoMedDb rồi restart API. Lượt triển khai không tự áp dụng migration hoặc ghi giao dịch lên database ứng dụng, không commit/push.

Lượt cuối: **546 assertions/130 ca FE mock; 329 UI qua API thật (178 smoke + 151 E2E); runner 295/0 (271 HTTP/SQL gồm 45 cash audit + 24 browser/database); SQL regression 51, time/slot 7**. Ca desktop mất phản hồi sau API commit đã đối soát đúng một khoản thu; desktop/mobile nhận 500, payment 420, change 80 và doanh thu 420. Các lượt trung gian lỗi điểm chờ/selector của test đã sửa rồi chạy lại toàn bộ, không gộp thành kết quả đạt. Build API/runner, TypeScript/lint/Vite đạt; dependency XML/bundle lớn có sẵn vẫn còn. DB/file/API/preview tạm đã dọn. Các nhận xét chưa lưu tender/chưa có idempotency ở phần lịch sử bên dưới không còn mô tả phiên bản mới này.


## Kết luận

> Cập nhật sau triển khai mục 3: mục 1 (khóa cấp phát/sửa đơn), mục 2 (tìm thuốc/tồn/dị ứng) và mục 3 (audit đọc/ghi bệnh án) đã xử lý. Xem [mục 1](prescription-dispensing-guards.md), [mục 2](prescription-search-stock-allergies.md), [mục 3](medical-record-audit.md). Số liệu/lỗi audit ban đầu bên dưới là lịch sử, không phải trạng thái hiện tại.

Luồng chính qua API thực tế đi được từ đặt lịch, xác nhận/check-in, khám, trả kết quả, chốt bệnh án đến lập hóa đơn, thu nhiều lần và phát thuốc. **Chưa thể kết luận workflow đã hoàn chỉnh**: các nhánh kiểm thử bất lợi tìm thấy lỗi liên quan đến cấp phát, tồn kho, dị ứng và nhật ký sửa bệnh án.

Phần audit ban đầu là bước kiểm tra/lập backlog, chưa sửa logic sản phẩm khi chạy lượt đó. Các mục triển khai sau được cập nhật riêng; chưa commit/push các thay đổi hiện tại.

Kết quả lượt audit ban đầu:

- HTTP/JWT: **64 assertions đạt, 4 không đạt** (tồn thuốc, nhật ký sửa, cấp phát đơn nháp, sửa đơn đã phát).
- Browser: **111 assertions đạt, 1 không đạt** (cảnh báo dị ứng khi vào URL trực tiếp). 44 lần tải trang ở hai viewport không gặp page error/API 5xx hoặc tràn ngang toàn trang.
- Runner báo tổng **64 đạt / 5 không đạt** vì tính browser suite thành một assertion tổng hợp; không cộng con số này với 112 assertions browser.
- Regression SQL: **51 checks đạt**. Không còn database `FoMed_Audit_*` sau kiểm tra.

## Cơ sở và phạm vi

- Đọc workflow và API mẫu tại [review-erd-net06](https://review-erd-net06.vercel.app/), tập trung VC-03/05/07/11/12–18/23/24. API trong review là hợp đồng tham chiếu, không mặc định tồn tại trong FoMed-API.
- Đối chiếu controller, DTO, service và API client thực tế trong FoMed-API, FoMed-Frontend; không phân tích FoMed-FE.
- Chạy HTTP thật qua ASP.NET Core, đăng nhập thật lấy JWT của tài khoản kiểm thử; không mock API cho bài kiểm tra trình duyệt.
- Tạo database ngẫu nhiên `FoMed_Audit_<GUID>` trên SQL Server local, schema và migration của dự án, tài khoản/dữ liệu tổng hợp. API kiểm thử chạy riêng trên cổng 5181; frontend preview dùng cổng 5184.
- Database tạm được xóa trong `finally`, chỉ tiến trình API do bài kiểm thử tạo mới bị dừng. Không tạo lịch/hồ sơ/hóa đơn kiểm thử trong FoMedDb.
- UI: 22 màn hình, 6 vai trò, viewport 1440px và 390px; đăng nhập bằng giao diện, kiểm tra tải trang, lỗi JavaScript, API 5xx, tràn ngang toàn trang, một số nút theo trạng thái và modal bệnh án.
- Chuyển trạng thái/thu tiền/phát thuốc được thực hiện bằng HTTP; **chưa phải toàn bộ thao tác workflow đều được bấm từ trình duyệt**. Không thay thế UAT thực tế, kiểm thử tải hoặc kiểm thử đầy đủ mọi kích thước màn hình.

## Ma trận luồng đã kiểm tra

| Bước / VC | Màn hình FE | API đang dùng trong FoMed-API | Kết quả |
| --- | --- | --- | --- |
| Đặt lịch — VC-03 | `/booking`, `/my-appointments` | `GET /api/appointments/available-slots`, `POST /api/appointments/book` | Lịch mới Pending; bệnh nhân không tự xác nhận được |
| Xác nhận/check-in — VC-07 | `/reception` | `PUT /api/appointments/{id}/confirm`, `PUT /api/appointments/{id}/check-in` | Lễ tân xác nhận; check-in lặp bị chặn |
| Hàng chờ — VC-12 | `/doctor/queue` | `GET /api/appointments/doctor-queue` | Lịch đã check-in xuất hiện trong hàng chờ bác sĩ phụ trách |
| Khám — VC-13 | `/doctor/exam/{recordId}` | `POST /api/clinical/appointments/{appointmentId}/record`, `PUT /api/clinical/records/{id}` | Tạo/sửa bệnh án và sinh hiệu được; bác sĩ khác không được sửa |
| Kê đơn — VC-15 | `/doctor/exam/{recordId}/prescription` | `POST/PUT /api/clinical/records/{id}/prescription` | Lưu đơn, chụp giá; API bắt xác nhận dị ứng; kiểm tra tồn còn thiếu |
| Chỉ định — VC-14 | `/doctor/exam/{recordId}/services` | `POST /api/clinical/records/{id}/services`, `PUT /api/clinical/orders/{id}/cancel` | Ordered hủy được; Completed không được hủy; giá chỉ định giữ snapshot |
| Kết quả — VC-14 | `/technician/orders` | `POST /api/clinical/lab-orders/{id}/result` | Chỉ kỹ thuật viên được nhập; chuyển Completed; chỉ định hủy không nhận kết quả |
| Chốt — VC-13 | `/doctor/exam/{recordId}` | `PUT /api/appointments/{id}/complete` | Chỉ định chờ ngăn chốt; chốt xong không sửa bệnh án; lần hai trả 409 |
| Bệnh nhân đọc — VC-05 | `/my-records` | `GET /api/clinical/records/{id}`, prescription/services cùng bệnh án | Chỉ đọc sau chốt; bệnh nhân khác bị chặn; modal có 3 bảng |
| Hóa đơn/thu tiền — VC-11 | `/reception/cashier`, `/reception/cashier/{invoiceId}` | `POST /api/invoices`, `POST /api/invoices/{id}/payments` | Không lập từ bản nháp; chặn trùng/vượt tiền; thu từng phần và thu đủ được |
| Phát thuốc — VC-16/17 | `/pharmacy/dispense/{prescriptionId}`, `/pharmacy/inventory` | `POST /api/pharmacy/prescriptions/{id}/dispense`, `POST /api/pharmacy/inventory/receipts` | FEFO chia 2 lô đúng; thiếu tồn bị chặn; bấm lại không trừ kho lần hai; thiếu điều kiện bệnh án đã chốt |
| Đối soát — VC-23 | `/admin/reports` | `GET /api/reports/summary` | Doanh thu theo payments và nợ còn lại khớp fixture |
| Nhật ký — VC-24 | `/admin/audit-logs` | `GET /api/audit-logs` | Có log đọc bệnh án; thiếu log sửa bệnh án |

### Đối soát tiền kiểm thử

Giá trị dưới đây là số tổng hợp nhỏ để dễ kiểm chứng, không phải giá dịch vụ thực tế:

- Phí khám: 300; dịch vụ Completed: 2 × 100; thuốc: 5 × 10; tổng hóa đơn: **550**.
- Chỉ định thứ hai hủy trước khi có kết quả: 3 × 200 **không tính tiền**.
- Đổi giá danh mục dịch vụ thành 999 và thuốc thành 99 sau khi kê/chỉ định: hóa đơn vẫn dùng snapshot ban đầu.
- Thu lần một 200: báo cáo `collectedAmount = 200`, `outstandingAmount = 350`.
- Thu lần hai 350: báo cáo `collectedAmount = 550`, `outstandingAmount = 0`.
- Bệnh nhân khác không đọc được bệnh án hoặc hóa đơn của fixture chính.

## Lỗi tái hiện và ưu tiên xử lý

### P1 — Phát đơn thuốc khi bệnh án chưa chốt

1. Lễ tân xác nhận/check-in lịch thứ hai.
2. Bác sĩ tạo bệnh án nháp và lưu đơn thuốc, chưa chẩn đoán/chốt, chưa có hóa đơn.
3. Dược sĩ gọi `POST /api/pharmacy/prescriptions/{id}/dispense`.
4. Thực tế: HTTP **200**, trừ tồn và ghi cấp phát.

`PharmacyService.DispenseAsync` kiểm tra role/tồn nhưng không kiểm tra bệnh án/lịch hẹn đã chốt. UI `PharmacyDispensePage` nhập ID rồi xác nhận trực tiếp, không có bước xem trạng thái đơn.

Đề xuất: API chặn đơn từ bệnh án chưa chốt; FE hiển thị trạng thái và preview đơn trước xác nhận. Điều kiện **phải thanh toán đủ trước phát thuốc** cần chốt riêng với nghiệp vụ, không tự coi là quy tắc bắt buộc vì VC-16 chưa quy định rõ.

### P1 — Sửa đơn đã phát trả 500

Sau khi phát đơn nháp ở trên, gọi `PUT /api/clinical/records/{recordId}/prescription` để đổi số lượng.

- Thực tế: HTTP **500**, không phải lỗi nghiệp vụ có thể xử lý rõ ràng.
- `UpdatePrescriptionAsync` xóa dòng đơn cũ trước khi tạo lại; dòng đã có `PrescriptionDispense` còn được tham chiếu. Đối chiếu code/schema cho thấy xung đột khóa ngoại là nguyên nhân cần xử lý bằng kiểm tra trước, không bằng thông báo chung.
- Đề xuất: trả 409 trước khi xóa/sửa đơn đã phát; transaction bảo toàn đơn và phân bổ cũ; FE chuyển sang chỉ đọc/hiện lý do. Không cho sửa lịch sử cấp phát để vượt lỗi.

### P1 — VC-15 vẫn cho kê thuốc không còn tồn

Fixture chưa có bất kỳ lô thuốc nào, API vẫn lưu đơn số lượng 5 và trả **201**. Thiếu tồn chỉ được phát hiện ở bước phát thuốc (409), quá muộn so với VC-15.

- `FillPrescriptionItemsAsync` kiểm tra thuốc tồn tại/active, không tính tồn khả dụng theo lô chưa hết hạn.
- `GET /api/clinical/medicines` chỉ trả id/name/price, không có tồn khả dụng, hoạt chất hoặc cờ dị ứng theo thuốc.
- FE đang dùng danh mục trang đầu, chưa có tìm thuốc đúng hợp đồng trong review.
- Đề xuất: bổ sung API tìm thuốc/tồn khả dụng và kiểm tra lại khi lưu đơn; không trừ tồn ở bước kê đơn. Đồng thời giữ kiểm tra khi cấp phát vì tồn có thể thay đổi giữa hai bước.

### P1 — Cảnh báo dị ứng mất khi vào URL trực tiếp/tải lại

Bệnh nhân fixture có `Allergies = "Audit Penicillin"`. API yêu cầu bác sĩ xác nhận trước khi kê, nhưng trang đơn thuốc vào trực tiếp không lấy lại dữ liệu dị ứng từ server.

`DoctorPrescriptionPage` lấy `allergies` từ `location.state`; DTO bệnh án không có trường này. Khi không có state, UI hiển thị “Chưa ghi nhận dị ứng”, có thể gây hiểu nhầm.

Đề xuất: trả dữ liệu ngữ cảnh khám có kiểm soát quyền (bao gồm dị ứng) từ API, không dùng navigation state làm nguồn dữ liệu nghiệp vụ; kiểm thử reload và deep link.

### P1 — VC-24 chưa ghi nhận sửa bệnh án

API cập nhật bệnh án thành công, sau đó truy vấn audit theo `MedicalRecord`: có Read nhưng không có Update tương ứng bệnh án. `UpdateRecordAsync` không thêm log sửa.

Đề xuất: ghi create/update/finalize cùng transaction nghiệp vụ, người thực hiện, bệnh án, thời điểm và thông tin thay đổi phù hợp; bảo vệ log khỏi sửa/xóa. IP chưa được trả trong DTO hiện tại cũng là khoảng trống so với review. Không đưa dữ liệu y tế nhạy cảm tràn lan vào log/thông báo.

### P2 — Giá hiển thị trên đơn đã chốt chưa dùng snapshot

Quan sát ảnh trang kê đơn sau khi đổi giá danh mục từ 10 thành 99: select đang hiển thị “Audit medicine · 99 đ”, trong khi giá snapshot đơn thuốc và hóa đơn vẫn là 10.

Đối chiếu `DoctorPrescriptionPage`: option lấy giá từ catalog, dữ liệu đơn được map vào form không giữ `unitPriceSnapshot`. Đây là phát hiện qua ảnh/code, **chưa có assertion tự động riêng**.

Đề xuất: đơn đã lưu/chốt cần hiển thị giá snapshot; giá danh mục hiện tại chỉ dùng khi chọn thuốc mới và phải ghi nhãn rõ nếu hiển thị cả hai.

## Khoảng trống so với review, không gắn nhãn đã hoàn thành

1. **VC-14 đính kèm / VC-05 tải file:** mục 4 đã bổ sung upload/list/download có quyền, kiểm tra đuôi/chữ ký/kích thước và kho riêng. Cần chạy migration metadata trên DB ứng dụng + restart API; chưa có antivirus/DICOM viewer hoặc migration file cũ. Xem `docs/clinical-workflow-item4.md`.
2. **VC-16 preview phát thuốc và báo thiếu:** mục 4 đã có danh sách chọn đơn và preview FEFO chia lô/tồn/thiếu trước cấp phát. Chưa có API báo thiếu thuốc gửi bác sĩ; không coi phần notification đã hoàn thành.
3. **Kỹ thuật viên — lịch sử kết quả:** mục 4 đã chuyển `/technician/results` sang server, chỉ kết quả do kỹ thuật viên này trả, phân trang/tìm kiếm và file theo kết quả; không còn dùng sessionStorage làm nguồn dữ liệu.
4. **PDF/in:** chưa có endpoint PDF đơn thuốc/hóa đơn. `window.print()` không tương đương PDF theo hợp đồng review; chưa nghiệm thu bản in chuyên dụng.
5. **Dị ứng chi tiết:** API hiện yêu cầu xác nhận chung theo trường chuỗi Allergies, chưa đối chiếu active ingredient với từng dị ứng như VC-15.
6. **Phân trang danh mục:** sau mục 2, thuốc đã có tìm kiếm/phân trang theo API mới. Select dịch vụ còn gọi trang đầu catalog 20/trang; cần xử lý riêng khi danh mục dịch vụ lớn.

## Bộ kiểm thử và cách chạy lại

File mới:

- `tests/ClinicWorkflow/HttpWorkflowAudit.cs`: HTTP/JWT, ca chính và nhánh sai thứ tự; database tách biệt, kết quả trong `tests/ClinicWorkflow/bin/audit-results/http-workflow.json` (ignored).
- `FoMed-Frontend/tests/clinic-workflow.smoke.cjs`: UI smoke với API thật của fixture; ảnh và JSON tại `FoMed-Frontend/dist/review-clinic/` (ignored).

Chuẩn bị SQL Server local và quyền tạo/xóa database. Chạy từ thư mục gốc; không chạy seed lên FoMedDb:

```powershell
dotnet build FoMed-API/FoMed.Api/FoMed.Api.csproj --no-restore -o FoMed-API/FoMed.Api/bin/WorkflowAudit
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj --no-restore -o tests/ClinicWorkflow/bin/HttpAudit
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http
```

Để thêm browser smoke:

1. Build frontend hiện tại: `npx.cmd tsc -b` và `npx.cmd vite build --configLoader runner` trong FoMed-Frontend.
2. Chạy preview trong terminal riêng:

```powershell
$env:VITE_API_PROXY_TARGET = 'http://127.0.0.1:5181'
npx.cmd vite preview --configLoader runner --host 127.0.0.1 --port 5184 --strictPort
```

3. Playwright và Chromium cần có sẵn; đặt `PLAYWRIGHT_MODULE` đến package Playwright nếu dùng bản cache ngoài dự án. Chạy runner từ thư mục gốc với `--http --browser`; không chạy browser script trực tiếp vào FoMedDb. Runner truyền ID fixture bằng môi trường, không lưu token/mật khẩu thật vào artifact.
4. Dừng preview khi xong. Runner tự dừng API kiểm thử/xóa database tạm. Cổng 5181 phải trống; nếu bị chiếm, runner từ chối trước khi tạo dữ liệu.

Exit code 1 là kết quả dự kiến **khi những lỗi nghiệp vụ trên vẫn tồn tại**, không phải mọi lỗi đều là lỗi hạ tầng. Sau khi sửa phải cập nhật fixture để tách hoàn toàn ca có tồn hợp lệ với ca cố tình kê hết tồn, rồi chạy regression lại.

## Ghi chú về regression và giới hạn

- Bộ `ClinicWorkflow --sql` cũ có assertion chốt lần hai chờ 400, đã cập nhật thành 409 đúng hợp đồng hiện tại; **không thay đổi API**.
- Hai migration trong fixture SQL cũ có `USE FoMedDb`; đã loại bỏ câu chuyển database ở bộ kiểm thử để đảm bảo migration chạy trong database tạm. Runner HTTP loại bỏ/kiểm tra câu lệnh chuyển database trước khi thực thi mọi migration.
- Regression SQL service: 51 checks vượt qua, gồm đặt lịch đồng thời, sở hữu bệnh án, đơn thuốc, kết quả và thanh toán đồng thời.
- Các bộ kiểm thử phân quyền/auth có sẵn và 18 checks hồ sơ bác sĩ mặc định vượt qua. Không dùng kết quả mặc định để nhận là đã chạy mọi tích hợp database hồ sơ bác sĩ.
- FE lint, TypeScript và Vite build vượt qua; còn cảnh báo bundle lớn. API build có cảnh báo NU1903 cho dependency `System.Security.Cryptography.Xml` 9.0.0, cần xử lý riêng trước triển khai.
- Sau mục 4 đã kiểm thử upload/download thật trên kho tạm qua HTTP/UI và DICOM Part 10 signature; chưa kiểm thử tải, nội dung DICOM bằng parser/viewer, antivirus, hoàn tiền, bản in, mọi trạng thái NoShow, phân trang dataset lớn hoặc full click-through workflow. Hai viewport smoke không khẳng định tất cả responsive đều hoàn hảo.

## Thứ tự triển khai tiếp

**Cập nhật sau mục 3:** mục 1–3 đã xử lý. Tìm thuốc có phân trang/tồn lô hợp lệ, POST/PUT chặn hết tồn/vượt tồn, dị ứng lấy từ API và phục hồi reload/deep link. Audit tạo/sửa/chốt, đơn/chỉ định/kết quả có transaction; log đọc theo từng ID và metadata người thực hiện tại thời điểm thao tác. Không còn bỏ assertion audit ghi khi chạy full `--http --browser`. Các lượt có flag focus ở mục 1/2 chỉ là kết quả lịch sử. Xem `docs/medical-record-audit.md` cho hợp đồng hiện tại và giới hạn phạm vi/proxy/legacy.

Lượt full sau mục 3: **164 HTTP + 152 UI đạt**, runner 167/0 (3 kiểm tra tổng hợp/browser/DB), SQL 51 và time/slot 7 đạt. Kiểm tra VC-24 gồm transaction rollback khi audit lỗi, danh tính/vai trò snapshot, header giả, legacy, phân trang 10, ngày UTC+07, lọc rõ ràng và modal/focus/lỗi mạng. Không có page error/API 5xx khi tải UI bình thường; 3 HTTP 500 của fault injection là cố ý. Database tạm đã xóa và process kiểm thử đã dừng.

1. Chặn cấp phát đơn chưa chốt và sửa đơn đã cấp phát; xử lý 409 và bảo toàn transaction.
2. Tìm thuốc + tồn khả dụng + dữ liệu dị ứng server; sửa reload/deep link.
3. Hoàn thiện audit ghi bệnh án và ngữ cảnh người truy cập.
4. Bổ sung preview/lựa chọn đơn cho dược sĩ, lịch sử kết quả kỹ thuật viên và attachment theo API đã thống nhất.
5. Chạy lại ca chính/ca lỗi bằng HTTP và mở rộng thao tác thực hiện hoàn toàn qua UI; sau đó mới nghiệm thu workflow.

**Cập nhật sau mục 4:** ba phần của mục 4 đã triển khai; hợp đồng/điều kiện vận hành tại `docs/clinical-workflow-item4.md`. Lượt full đạt **226 HTTP + 178 UI**, runner 229/0 (thêm 3 kiểm tra tổng hợp/browser/DB), SQL 51 và time/slot 7; regression auth/phân quyền đạt. Browser mở 23 route/46 lượt viewport, thao tác upload/download từ ba vai trò, tên file tiếng Việt, liên kết file khi ID kết quả khác ID chỉ định, lưu kết quả và phục hồi lịch sử, chọn đơn/phân trang/preview/xác nhận FEFO. Không có JS error/API 5xx trong tải UI bình thường; 4 HTTP 500 fault injection là cố ý. DB/kho file tạm đã xóa, API/preview kiểm thử đã dừng; chưa chạy migration trên DB ứng dụng, chưa commit/push.

**Cập nhật sau mục 5:** đã hoàn thành hai lượt khám xuyên suốt qua UI với API thật trên DB tạm, ở 1440px và 390px; fixture chỉ tạo tài khoản/danh mục/lịch làm việc/tồn, không seed giao dịch của hai bệnh nhân. Bao gồm đặt lịch, xác nhận/check-in, khám/chỉ định/hủy Ordered, trả kết quả/file, kê đơn/chốt, lập hóa đơn/thu một phần và đủ, báo cáo payments/công nợ, FEFO, bệnh nhân xem bệnh án/tải file và nhật ký chốt. Đã sửa thiếu UI lập hóa đơn, ngày tái khám rỗng, guard thao tác đồng thời, giới hạn số lượng chỉ định, báo cáo lọc lại và nhãn chỉ định hủy.

Lượt full mới nhất: **320 UI đạt (178 smoke + 142 E2E), runner 248/0 (226 HTTP + 3 kiểm tra tổng hợp cũ + 19 đối chiếu dữ liệu sau UI)**; SQL 51, time/slot 7 và regression auth đạt. Lint/TypeScript/Vite/build API đạt, cảnh báo dependency/bundle còn nguyên. Không có lỗi JS/API 5xx trong luồng UI bình thường hoặc tràn ngang document ở các màn hình được kiểm tra. DB/kho file và API/preview kiểm thử đã dọn; không áp dụng migration hoặc tạo giao dịch lên DB ứng dụng, chưa commit/push. Chi tiết tại `docs/clinical-workflow-item5.md`.

Mục 5 không đồng nghĩa đã nghiệm thu trên dữ liệu thật hoặc mọi nhánh workflow. Ca lỗi mạng hiện chặn trước server; chưa mô phỏng khoản thu đã commit rồi mất response, và POST payments chưa có idempotency key. Cần xử lý chống ghi nhận lặp/đối soát trước vận hành thu tiền thật. Sau đó ưu tiên backlog theo lựa chọn sản phẩm: notification thiếu thuốc, PDF/bản in, dịch vụ lớn, dị ứng theo hoạt chất và nghiệm thu có kiểm soát trên môi trường triển khai.

**Cập nhật quyết định thu ngân:** frontend chỉ thu đủ số dư; tiền mặt nhập tiền khách đưa và đối chiếu tiền thừa, không tạo khoản thu một phần. API thu một phần giữ nguyên cho tương lai. Luồng UI thật nay nhận 500, ghi một khoản thu 420, trả lại 80; patient invoice, báo cáo và SQL kiểm tra không tính tiền khách đưa/tiền thừa vào doanh thu. Tiền khách đưa/tiền thừa chưa lưu riêng trong API. Lượt full cuối đạt **322 UI (178 smoke + 144 E2E), runner 248/0 và time/slot 7**; regression riêng tiền thừa với mock API đạt **500 assertions/128 trường hợp**. TypeScript/lint/Vite/build runner đạt, DB/file/API/preview kiểm thử đã dọn, không thay đổi FoMedDb, không commit/push. Chi tiết tại `FoMed-Frontend/docs/MONEY-INPUT-AUDIT.md`; mock response bị mất chỉ xác minh refresh trạng thái phía FE, không phải nghiệm thu idempotency backend.

# Mục 5 — Kiểm thử UI liên vai trò và sửa điểm đứt workflow

## Cập nhật mới — lưu giao dịch tiền mặt và đối soát

Đã bổ sung backend lưu `cashReceived`, người thu từ tài khoản xác thực và snapshot tên, server tính `changeAmount`. Lịch sử thu ngân/hóa đơn bệnh nhân lấy từ API và phục hồi sau reload, dữ liệu cũ hiển thị chưa ghi nhận. UUID theo người thu chống ghi nhận lặp; cùng payload replay, khác payload trả 409. FE giữ key cho retry trong lượt đang mở, kiểm tra GET sau lỗi trước thao tác tiếp. API thu một phần vẫn được giữ nguyên.

Cần execute `database/migrations/20261005_add_payment_cash_audit.sql` trên đúng FoMedDb **trước** khi chạy API mới. Chưa áp dụng lên database ứng dụng. Chi tiết hợp đồng/triển khai/giới hạn tại `docs/payment-cash-audit.md` ở repository API.

Regression FE mới nhất: **546 assertions / 130 trường hợp**, mock toàn bộ API ở desktop/mobile, không phải giao dịch thực. Workflow thật lượt cuối đạt **329 UI (178 smoke + 151 E2E), runner 295/0 (271 HTTP/SQL, gồm 45 cash audit, + 24 browser/database), SQL 51 và time/slot 7**. Desktop có ca mất response sau API commit, GET đối soát đúng một payment; cả desktop/mobile phục hồi tiền thừa/người thu sau reload và doanh thu vẫn theo amount, không theo tender. Build API/runner, TypeScript/lint/Vite đạt. Các lượt trung gian lỗi điểm chờ/selector của test đã sửa và chạy lại toàn bộ, không gộp vào kết quả đạt. DB/kho file/API/preview tạm đã dọn; không chạy migration lên FoMedDb, không commit/push. Các kết quả và nhận xét ‘chưa lưu metadata/chưa có idempotency’ bên dưới là lịch sử trước bổ sung này.


Phạm vi: FoMed-Frontend và hợp đồng FoMed-API hiện có. Không dùng FoMed-FE, không tạo thêm module, không commit/push. Đây là kiểm thử tích hợp với API thật trên database tạm, không phải mock toàn bộ backend hoặc nghiệm thu dữ liệu bệnh nhân thật.

## Cập nhật nghiệp vụ thu ngân

Sau nghiệm thu ban đầu bên dưới, người dùng chọn UI chỉ thu đủ số dư, không tạo thanh toán một phần. Tiền mặt nhập Tiền khách đưa, tính Tiền thừa cần trả và chỉ POST số dư. Backend thu một phần giữ nguyên cho phát triển sau. E2E hiện nhận 500, thu vào hóa đơn 420 và trả lại 80; dữ liệu SQL/patient invoice/report phải có đúng một khoản thanh toán 420, không có khoản thu 500. Regression frontend riêng đạt 500 assertions/128 trường hợp với API giả; chi tiết và giới hạn tại `FoMed-Frontend/docs/MONEY-INPUT-AUDIT.md`.

Các bước/kết quả 320 UI bên dưới là lịch sử nghiệm thu trước quyết định này. Lượt full cuối theo nghiệp vụ mới đạt **322 UI (178 smoke + 144 E2E), runner 248/0 (226 HTTP + 22 browser/database), time/slot 7**; TypeScript/lint/Vite và build runner đạt. HTTP thu một phần vẫn được kiểm thử đạt. DB/kho file và API/preview kiểm thử đã dọn; không thay đổi FoMedDb, không commit/push. Không gộp các lượt lỗi trung gian thành kết quả đạt. Tiền khách đưa/tiền thừa chỉ có tại UI hiện tại, chưa được lưu riêng trong API.

## Hai lượt khám tạo hoàn toàn qua UI — nghiệm thu ban đầu

Mỗi viewport 1440px và 390px có bệnh nhân riêng. Fixture chỉ tạo tài khoản, bác sĩ/lịch làm việc, danh mục dịch vụ/thuốc và tồn lô. Không seed lịch hẹn, bệnh án, chỉ định, kết quả, đơn thuốc, hóa đơn hoặc thanh toán của hai bệnh nhân này.

1. Bệnh nhân chọn bác sĩ/slot, nhập lý do, đặt lịch và thấy Chờ xác nhận.
2. Lễ tân xác nhận, check-in, xem bệnh nhân trong hàng chờ; bệnh nhân reload thấy Đã xác nhận.
3. Bác sĩ bắt đầu khám, nhập bệnh án/sinh hiệu, lưu và reload; thêm hai chỉ định, hủy một chỉ định Ordered; đính kèm PDF.
4. Chốt khi còn Ordered phải bị từ chối 409 và giữ bệnh án chỉnh sửa được. Kỹ thuật viên nhập kết quả cho chỉ định còn lại, mở lịch sử server và đính kèm PNG vào đúng kết quả.
5. Bác sĩ thấy kết quả Completed, không còn quyền hủy chỉ định đó; kê thuốc với xác nhận đã kiểm tra dị ứng rồi chốt lượt khám. Bệnh án được khóa sửa.
6. Lễ tân chọn Chờ lập hóa đơn, xác nhận lập từ bệnh án đã chốt; API tính phí theo snapshot. Thu hai lần 200 + 220; khoản thu vượt số dư bị chặn, hóa đơn thu một phần không hủy được.
7. Admin lọc đúng bác sĩ kiểm thử: doanh thu theo payments, còn nợ sau thu một phần, hết nợ sau thu đủ. Lọc lại cùng bộ lọc phải lấy dữ liệu mới; CSV lấy từ API.
8. Dược sĩ chọn đơn, xem FEFO, xác nhận phát hai đơn vị; reload không cho phát lại. Lượt desktop chia lô hạn gần nhất 1 + lô kế tiếp 1, lượt mobile lấy 2 từ lô còn lại.
9. Bệnh nhân xem bệnh án từ lịch hẹn hoàn tất: ba bảng, đơn thuốc, kết quả; tải PDF/PNG và đối chiếu bytes. Xem hóa đơn và hai khoản thanh toán thật. Không truy cập được báo cáo admin.
10. Admin thấy nhật ký chốt đúng bệnh án/bác sĩ, không lộ nội dung chẩn đoán. Đối chiếu lại database: duy nhất một lịch/bệnh án/hóa đơn mỗi bệnh nhân, hai khoản thu, Completed/Canceled tách biệt, owner file đúng, lượng xuất/tồn đúng.

## Những chỉnh sửa frontend

- Trang thu ngân trước đây thiếu thao tác lập hóa đơn. Nay `/reception/cashier` có hai nhóm Chờ lập hóa đơn/Hóa đơn đã lập, phân trang API 20 kết quả. Có modal xác nhận, thông báo lỗi giữ modal để xử lý, chống gửi trùng và chuyển đến chi tiết hóa đơn khi tạo thành công. Dùng `GET /api/invoices/eligible`, `POST /api/invoices`, không bổ sung API hoặc gửi giá/tổng tiền từ client.
- Không có tổng số kết quả trong API hóa đơn/eligible hiện tại: nút Sau dựa vào đủ 20 dòng, có thể đến trang rỗng ở ranh giới đúng bội số 20; không hiển thị tổng trang giả.
- Trường ngày tái khám rỗng được gửi `null` thay vì chuỗi rỗng gây lỗi deserialize `DateOnly?`; áp dụng cả tạo/lưu bệnh án tại adapter clinical API. Đây không phải bỏ validation server.
- Khi lưu/chốt bệnh án, khóa thao tác còn lại và trường nhập để không chồng request hoặc mất chỉnh sửa do reload. Kiểm tra ID/đang tải/lỗi/chốt trước gửi.
- Số lượng chỉ định phải là số nguyên 1–1000, đúng DTO API. Không tự biến 0/chuỗi rỗng thành 1. Khóa thêm chỉ định khi catalog chưa tải được.
- Đặt lịch kiểm tra slot đang chọn còn hợp lệ, khóa xác nhận khi tải/lỗi, tải lại slot sau request; lỗi giữ lý do. Check-in có busy guard và khóa nút.
- Thu tiền có guard theo trạng thái, số dư và request đang chạy; thông báo tạo hóa đơn thành công tại trang chi tiết. Trả kết quả khóa trường nhập trong khi lưu.
- Báo cáo tải lại khi bấm Lọc dù bộ lọc không đổi, để cập nhật khoản thu vừa phát sinh.
- Modal bệnh án ghi rõ “Đã hủy” cho dịch vụ Canceled, không hiển thị nhầm thành chưa có/đang chờ kết quả.

## Cách chạy và an toàn

Từ thư mục gốc build API sang `FoMed-API/FoMed.Api/bin/WorkflowAudit`, build `tests/ClinicWorkflow` sang `tests/ClinicWorkflow/bin/HttpAudit`. Build frontend (TypeScript, Vite), mở preview 5184 với `VITE_API_PROXY_TARGET=http://127.0.0.1:5181`. Playwright/Chromium phải có sẵn; `PLAYWRIGHT_MODULE` chỉ đến package bên ngoài nếu cần.

```powershell
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --http --browser
```

- `tests/clinic-workflow.smoke.cjs` chạy regression các trang/hành động cũ rồi gọi `tests/clinic-workflow.e2e.cjs` để chạy hai lượt khám mới. `--journey-only` là lượt kiểm thử tập trung khi sửa E2E, bỏ smoke cũ; không dùng kết quả đó thay cho lượt full cuối.
- API riêng ở cổng 5181. Runner kiểm tra cổng trước khi tạo DB, chỉ áp dụng schema/migration vào DB tên random đã kiểm soát; loại bỏ USE FoMedDb và từ chối câu DDL chuyển DB. Không chạy seed/migration/reset lên FoMedDb.
- Mạng lỗi được mô phỏng bằng chặn request booking/payment trong browser; không giả response thành công. Request chậm được giữ để thử nút disabled/chống gửi lặp. Các lần thành công đều gọi API thật.
- Runner kiểm tra SQL sau UI, tự dừng API sở hữu, xóa DB và file tạm. Preview được dừng riêng. Artifact JSON/PNG trong `FoMed-Frontend/dist/review-clinic/` và HTTP JSON trong `tests/ClinicWorkflow/bin/audit-results/` được ignored; không lưu token hoặc dữ liệu người dùng thật.

## Giới hạn

Hai luồng tiêu biểu không chứng minh mọi nghiệp vụ hoặc mọi kích thước màn hình đã hoàn chỉnh. Chưa nghiệm thu trên DB ứng dụng bằng tài khoản/dữ liệu thật; không tự đặt lịch, lập hóa đơn hoặc thu tiền thật của người dùng để kiểm thử. Chưa kiểm thử tải đồng thời lớn, trải nghiệm in thực tế, PDF hóa đơn, hoàn tiền, scanner/AV/DICOM viewer, notification báo thiếu thuốc, dị ứng theo hoạt chất và mọi nhánh đổi lịch/NoShow. Cảnh báo dependency backend và bundle frontend cần xử lý riêng.

Các ca mạng lỗi hiện chặn **trước khi request tới server**. Chưa mô phỏng việc server đã commit khoản thu nhưng response bị mất. POST payments hiện chưa có idempotency key; busy guard phía UI không bảo đảm retry trong tình huống đó không tạo khoản thu tiếp theo. Cần thiết kế đối soát/idempotency phía API trước vận hành thu tiền thật. Đây là rủi ro suy ra từ hợp đồng hiện có, không phải ca đã kiểm thử đạt trong mục 5.

## Kết quả lượt full ngày 05/10/2026

- UI: **320 kiểm tra đạt, 0 lỗi** — 178 smoke/regression và 142 kiểm tra hai lượt khám xuyên suốt. Chạy ở viewport 1440px và 390px; không có lỗi JavaScript chưa xử lý, API 5xx trong luồng UI bình thường hoặc tràn ngang document ở những màn hình được kiểm tra. Bảng có cuộn ngang bên trong trên mobile không bị coi là tràn toàn trang.
- Runner HTTP/browser/database: **248 đạt, 0 lỗi** — 226 kiểm tra HTTP, 3 kiểm tra tổng hợp/browser/database cũ và 19 kiểm tra dữ liệu sau hai lượt UI. Bốn HTTP 500 của ca cố tình gây lỗi transaction là dự kiến, không phải lỗi ở luồng sử dụng bình thường.
- Regression SQL: **51 kiểm tra đạt**; thời gian/slot: **7 kiểm tra đạt**. Regression phân quyền/auth đạt, bao gồm quyền lịch hẹn, controller guard, login/refresh/đăng ký/đổi mật khẩu.
- Frontend lint, TypeScript, Vite build và build API/test runner đạt. Còn cảnh báo bundle frontend lớn và NU1903 của `System.Security.Cryptography.Xml` 9.0.0; chưa xử lý trong mục này.
- API/preview kiểm thử đã dừng; runner đã xóa database và kho file tạm. Không áp dụng SQL/migration lên FoMedDb, không tạo dữ liệu khám/thu tiền trong DB ứng dụng, không commit/push.

Đây là nghiệm thu hai workflow đại diện cùng regression hiện có, **không phải xác nhận toàn bộ dự án sẵn sàng vận hành**. Ưu tiên tiếp theo là cơ chế chống ghi nhận thanh toán lặp/đối soát khi mất phản hồi, sau đó chọn backlog sản phẩm trong phần Giới hạn.

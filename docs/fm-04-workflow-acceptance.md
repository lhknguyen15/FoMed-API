# FM-04 — Kiểm thử quy trình FoMed

Ngày kiểm tra: **07/10/2026**. Trạng thái: **đạt phần kiểm thử tự động cục bộ;
chưa nghiệm thu giao diện toàn luồng trên bản triển khai**.

Không commit/push/deploy trong bước này. Không đọc cấu hình Development riêng,
không ghi vào FoMedDb/Azure, không gọi SePay thật hoặc chuyển tiền thật.

## Kết quả của lượt chạy cuối

| Nhóm kiểm tra | Kết quả | Phạm vi |
| --- | --- | --- |
| Quy trình HTTP/JWT/SQL | 321 đạt, 0 lỗi | Có 50 kiểm tra mới; gồm 9 lần đăng nhập và 45 kiểm tra thu tiền mặt |
| SePay mô phỏng HTTP/HMAC/SQL | 129 đạt, 0 lỗi | Giao dịch giả gửi vào API localhost; gồm 9 lần đăng nhập |
| Dịch vụ/lịch hẹn SQL | 51 đạt | Gồm 7 kiểm tra thời gian/khung giờ; có truy cập đồng thời |
| Cấu hình/chữ ký SePay | 31 đạt | Chạy kèm runner; không cộng lặp giữa các lượt |
| Frontend React ngoại tuyến | 264 đạt | Thuốc 38, lịch 44, in hóa đơn 64, kết quả/lọc 24, trạng thái đã thu 32, SePay thành công 22, lọc hóa đơn 16, thông báo 24 |
| Hồi quy khác | Đạt | Phân quyền/tài khoản, báo cáo 27, lịch sử khám 27, tiếp tục khám 17, tìm hóa đơn 36 |
| Build API/runner | Đạt | Release, 0 cảnh báo, 0 lỗi |
| Frontend lint/TypeScript/Vite | Đạt | Còn cảnh báo gói JavaScript trên 500 KB; không phải lỗi build |

Không cộng các nhóm thành số tình huống độc lập: một số ca kiểm tra có thể trùng
mục tiêu giữa các bộ. Các lượt trung gian sai fixture/helper đã sửa và chạy lại;
không dùng chúng làm bằng chứng nghiệm thu đạt. Chưa phát hiện lỗi nghiệp vụ mới
trong lượt chạy cuối; thay đổi của FM-04 nằm ở bộ kiểm thử và tài liệu.

## Những gì đã được kiểm chứng

1. Admin tạo, tìm, sửa, ngừng sử dụng thuốc qua HTTP; giá và trạng thái được lưu,
   không tự tạo tồn kho, không ghi đè bằng phiên bản cũ; vai trò khác bị từ chối.
2. Lễ tân tạo ca làm việc; API đặt lịch trả đúng khung giờ và bệnh nhân đặt được
   lịch theo ca mới. Không cho ca trùng, xóa/thu ngắn ca làm mất lịch bệnh nhân
   hoặc sửa bằng phiên bản cũ. Gia hạn an toàn được phép; lịch nghỉ chỉ được đọc.
3. Bệnh nhân đặt lịch → lễ tân xác nhận/tiếp đón → bác sĩ nhận hàng chờ và khám.
   Người khác không được đọc/sửa bệnh án không thuộc quyền của mình.
4. Bác sĩ chỉ định dịch vụ; kỹ thuật viên lưu kết quả; bác sĩ tải lại thấy trạng
   thái hoàn thành, kết quả, kết luận và thời gian trả rồi cập nhật đơn thuốc.
   Chỉ định còn chờ ngăn chốt bệnh án; chỉ định đã hủy không vào hóa đơn.
5. Thuốc hết tồn/hết hạn, số lượng vượt tồn và thiếu xác nhận dị ứng được xử lý.
   Kê đơn không trừ kho; hóa đơn dùng giá đã lưu, không đổi theo giá danh mục mới.
6. Hóa đơn có đúng tên/mã bệnh nhân, ngày lập UTC và tổng tiền. Đọc dữ liệu để in
   không tạo khoản thu; dữ liệu hóa đơn đã thanh toán khớp tổng các khoản thu.
   Bệnh nhân khác và bác sĩ không được lấy dữ liệu hóa đơn đó.
7. Khoản thu, tiền khách đưa, tiền thừa và người thu được lưu đúng. Thử lại/đồng
   thời không thu trùng; doanh thu theo số tiền đã thu, không theo tiền khách đưa.
   Thu một phần được kiểm tra ở API để giữ tương thích, không tuyên bố giao diện
   thu ngân hiện hỗ trợ chức năng này.
8. Cấp thuốc theo FEFO, bỏ lô hết hạn; thiếu thuốc không trừ một phần ngoài ý muốn.
   Cấp lại/đồng thời không trừ kho hai lần. Bệnh án nháp/trạng thái khám không nhất
   quán không được cấp thuốc. Các ca này không thêm điều kiện thanh toán mới.
9. Nhật ký, kết quả, tệp riêng, quyền tải tệp, phân trang và báo cáo được kiểm tra.
   Lỗi ghi nhật ký/database được chủ động tạo trong database tạm để kiểm tra
   rollback; các HTTP 500 cố ý này không phải lỗi trong luồng bình thường.
10. SePay: chữ ký sai, phát lại, chuyển thiếu/dư, sai tài khoản, mã cũ/hết hạn,
    cạnh tranh thu tiền mặt và webhook, lỗi database rồi thử lại. Giao dịch chưa
    đối soát không được âm thầm tính vào doanh thu; Test luôn giữ dấu mô phỏng.

## Cách chạy lại an toàn

Chạy tại root FoMed. Cần SQL Server trên **localhost**, Windows Integrated
Security và quyền tạo/xóa database kiểm thử. Runner không nhận server/credentials
từ appsettings hay tham số; chỉ chấp nhận tên `FoMed_Audit_<GUID>` hoặc
`FoMed_Test_<GUID>` đã kiểm tra trước khi tạo/dọn.

```powershell
dotnet publish FoMed-API/FoMed.Api/FoMed.Api.csproj -c Release --no-restore -o FoMed-API/FoMed.Api/bin/WorkflowAuditPublish
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj -c Release --no-restore
dotnet tests/ClinicWorkflow/bin/Release/net10.0/ClinicWorkflow.dll --http
dotnet tests/ClinicWorkflow/bin/Release/net10.0/ClinicWorkflow.dll --http --sepay-only
dotnet tests/ClinicWorkflow/bin/Release/net10.0/ClinicWorkflow.dll --sql
```

Dùng `dotnet restore` nếu máy chưa khôi phục dependency. Không build API vào
output cũ rồi chạy runner mới: runner yêu cầu thư mục publish riêng ở trên và từ
chối nếu có `appsettings.Development.json`. API dùng môi trường Audit, JWT giả,
cổng loopback ngẫu nhiên, không proxy/chuyển hướng HTTP và kho tệp riêng. Cuối
mỗi lượt, API con dừng và database/kho tệp tạm được xóa; dữ liệu thử nghiệm đó
không được giữ để phục hồi. Dữ liệu ứng dụng không bị xóa.

Luồng HTTP cần ít nhất 5 khung giờ còn trống trong ngày; nên chạy trước 22:30 giờ
Việt Nam. Nếu thiếu khung giờ, runner dừng và báo chưa hoàn tất, không bỏ qua ca.
Hai bộ SePay và thu tiền mặt riêng không phụ thuộc điều kiện này.

Các báo cáo sinh trong thư mục ignored:

- `tests/ClinicWorkflow/bin/audit-results/http-workflow.json`
- `tests/ClinicWorkflow/bin/audit-results/http-sepay.json`

Chỉ nghiệm thu khi process thoát mã 0, `completed=true` và `failed=0`.
Không đăng báo cáo chứa dữ liệu tài khoản/bệnh nhân thật. FM-04 không chạy các
flag/script trình duyệt; kiểm tra React không thay thế browser E2E.

```powershell
cd FoMed-Frontend
node tests/medicine-catalog.audit.cjs
node tests/reception-schedules.audit.cjs
node tests/invoice-print.audit.cjs
node tests/service-order-results.audit.cjs
node tests/cashier-paid-state.audit.cjs
node tests/sepay-success.audit.cjs
node tests/invoice-filters.audit.cjs
node tests/user-messages.audit.cjs
npm run lint
npm run build
```

## Nghiệm thu thủ công còn lại — chưa đánh dấu đạt

Sau khi người dùng quyết định commit/merge/deploy API rồi frontend, dùng **dữ
liệu giả** và ghi lại mã lịch hẹn, bệnh án, đơn thuốc, hóa đơn cho từng lượt.
Không chạy bộ kiểm thử local ở trên với connection string Azure.

- [ ] Một lượt đặt khám online và một lượt lễ tân tạo tại quầy, tiếp đón và hàng chờ.
- [ ] Bác sĩ khám, thêm chỉ định; kỹ thuật viên lưu kết quả; trang bác sĩ tự cập
  nhật đúng chỉ định, thông báo một lần rồi kê đơn/chốt bệnh án.
- [ ] Tiền mặt: tiền khách đưa/tiền thừa đúng; tải lại trang vẫn giữ khoản thu
  và người thu; bấm lại không thu thêm; dược sĩ cấp thuốc và tải lại kiểm tra tồn.
- [ ] SePay Test: kết quả thành công hiện ngay trong cửa sổ thanh toán mà không
  cần đóng cửa sổ chờ; xem hóa đơn; phát lại qua SePay không tạo khoản thu mới.
  Chỉ thực hiện mô phỏng được cho phép, không dùng ngân hàng thật.
- [ ] In hóa đơn A4 nhiều trang: logo, tiếng Việt, lề, đầu bảng và hàng cuối;
  không có menu/form thu tiền; bản lưu PDF đủ trang, rõ dấu mô phỏng/cảnh báo.
- [ ] Thử các vai trò và đường dẫn sâu; reload/session hết hạn không lộ dữ liệu
  cũ. Kiểm tra desktop/mobile, bàn phím, thông báo và lỗi mạng/API.
- [ ] Đối chiếu báo cáo, công nợ, lịch sử bệnh nhân và tồn kho của đúng lượt demo.

Không kiểm thử trực quan trong lượt này vì công cụ trình duyệt không khả dụng
theo chính sách phiên làm việc; không thay bằng tự động hóa trình duyệt khác.
Lưu trữ tệp bền vững trên cloud, giới hạn API, sao lưu, vận hành và thanh toán Live
vẫn là các hạng mục riêng, không được suy ra là đã hoàn tất từ kết quả local.

Bước phát triển tiếp theo: **FM-05 — giới hạn tần suất truy cập API**. Nghiệm thu
thủ công FM-04 vẫn còn mở và phải hoàn tất trước khi tuyên bố toàn hệ thống đạt.

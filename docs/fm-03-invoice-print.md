# FM-03 — Mẫu in hóa đơn riêng

## Sử dụng

Lễ tân mở **Thu ngân → Chi tiết hóa đơn → In hóa đơn**. Thao tác mở trang xem trước `/reception/cashier/{invoiceId}/print`, lấy lại hóa đơn đã lưu từ API, không in nguyên màn hình thu ngân.

Trang xem trước có **Về thu ngân**, **Tải lại**, **In hóa đơn**. Chỉ bấm In khi dữ liệu tải xong và kiểm tra hợp lệ. Có thể chọn **Lưu thành PDF** trong hộp thoại in của trình duyệt; đây không phải chức năng tải PDF trực tiếp từ hệ thống.

## Nội dung bản in

- Nhận diện FoMed, mã hóa đơn và ngày lập theo giờ Việt Nam.
- Tên/mã bệnh nhân, trạng thái hóa đơn.
- Phí khám riêng và từng dịch vụ/thuốc theo **khoản mục đã lưu trên hóa đơn**, không tra lại giá danh mục hiện tại.
- Tổng chi phí, tổng đã thu từ các payment, số tiền còn phải thu đối với hóa đơn chưa hủy.
- Các khoản thu đã lưu: ngày giờ, hình thức, người thu theo tên đã lưu; SePay kèm mã giao dịch nếu có.
- Tiền khách đưa và tiền thừa của giao dịch tiền mặt hiển thị riêng, không cộng vào doanh thu. Giao dịch cũ thiếu dữ liệu ghi “Chưa ghi nhận”, không đoán giá trị hay người thu.
- Hóa đơn chưa trả đủ ghi rõ chưa xác nhận thu đủ tiền. Hóa đơn đã hủy có cảnh báo và không hiển thị yêu cầu thu số dư.
- Bất kỳ khoản thu `providerEnvironment=Test` nào làm bản in có cảnh báo **CÓ GIAO DỊCH MÔ PHỎNG**, kể cả hóa đơn đã có trạng thái thanh toán.
- Dấu thời điểm tải dữ liệu để phân biệt bản in với thông tin cập nhật sau đó.

Không in thanh điều hướng, biểu mẫu thu ngân, thông báo nổi, ghi chú nội bộ, mã chống gửi lặp, mã QR/tài khoản chuyển tiền. Không tự thêm địa chỉ, điện thoại, mã số thuế, chữ ký hay khẳng định tính pháp lý của hóa đơn điện tử. Đây là phiếu thông tin chi phí và thanh toán trong FoMed.

## Bố cục và an toàn

Mẫu riêng dùng tên trang in `fomed-invoice`, khổ A4, lề 12 mm; không áp đặt khổ giấy lên chức năng in đơn thuốc. Quy tắc CSS lặp tiêu đề bảng, tránh tách dòng/hộp tổng kết, cho xuống dòng mô tả dài; không có vùng cuộn cắt bớt khoản mục. Bản in vẫn đọc được khi không in màu nền.

Frontend đối chiếu giá × số lượng, tổng khoản mục + phí khám, tổng khoản thu, số dư, trạng thái và tiền thừa theo đơn vị xu để không so sánh số thập phân bằng phép bằng dấu phẩy động. Thiếu thông tin nhận diện/ngày lập, tải lỗi, dữ liệu thuộc hóa đơn khác hoặc số liệu không khớp đều ẩn mẫu và khóa nút in. Khi phát hiện lỗi, tải lại và đối chiếu; không tự sửa khoản thu/hóa đơn.

Nút in tại thu ngân bị khóa khi đang gửi thanh toán, mở cửa sổ SePay, cần đối soát hoặc dữ liệu không thuộc hóa đơn hiện tại. Trang in không thu tiền, không xác nhận thanh toán, không hủy hóa đơn. Dữ liệu là ảnh chụp tại thời điểm tải; dùng Tải lại để nhận các khoản thu vừa phát sinh trên thiết bị khác.

Trang in yêu cầu `Receptionist` hoặc `Admin`. API hóa đơn vẫn giữ quyền hiện tại: thu ngân/quản trị viên và bệnh nhân sở hữu hóa đơn; việc thêm trường nhận diện không cấp quyền đọc mới. Không có đường dẫn in công khai hay cơ chế đưa token lên URL. Nội dung chuỗi được React thoát ký tự, không dùng HTML ghép từ dữ liệu bệnh nhân.

## Thay đổi backend và triển khai

`InvoiceResponse` thêm ba trường tùy chọn tương thích ngược: `patientName`, `patientCode`, `createdAt`.

- Tên ưu tiên `Invoice.PatientName` lưu lúc lập; hóa đơn cũ không có tên lưu riêng dùng tên bệnh nhân hiện tại. Mã bệnh nhân lấy từ hồ sơ hiện tại vì chưa có cột lưu mã riêng trên hóa đơn.
- Ngày lập khai báo UTC rõ ràng trong JSON; ngày giờ in chuyển sang `Asia/Ho_Chi_Minh`.
- Số tiền, trạng thái, giá khoản mục và khoản thu vẫn dùng dữ liệu cũ; không đổi công thức hoặc tạo payment khi in.

Không cần migration mới cho FM-03. Triển khai **API trước, frontend sau**. Nếu API chưa có ba trường, trang in thông báo thiếu thông tin thay vì in một hóa đơn thiếu tên/ngày.

Chưa commit, push, triển khai hoặc thay đổi dữ liệu Azure trong bước này. Cấu hình riêng đang sửa trong máy được giữ nguyên.

## Kiểm chứng tự động

```powershell
dotnet run --project tests/InvoicePrint -- --sql
dotnet run --project tests/InvoiceSearch
dotnet run --project tests/MedicineCatalog -- --sql
dotnet build FoMed-API/FoMed.Api/FoMed.Api.csproj -c Release --no-restore
```

```powershell
cd FoMed-Frontend
node tests/invoice-print.audit.cjs
node tests/cashier-paid-state.audit.cjs
node tests/sepay-success.audit.cjs
node tests/invoice-filters.audit.cjs
node tests/user-messages.audit.cjs
node tests/reception-schedules.audit.cjs
npm run lint
npm run build
```

Kiểm tra mới: **38 backend + 64 frontend**. Backend kiểm tra cùng bộ dữ liệu giả trong bộ nhớ và SQL Server cục bộ, gồm phân quyền, tên lưu riêng, hóa đơn cũ, UTC, khoản thu, tiền thừa, hóa đơn miễn phí và dấu mô phỏng. Chế độ SQL chỉ tạo/xóa cơ sở dữ liệu ngẫu nhiên `FoMed_Print_Test_<GUID>` trên `localhost`, xác nhận đích trước thao tác và xóa bằng `finally`. Không dùng appsettings riêng, Azure hoặc chuyển tiền thật.

Frontend kiểm tra xác thực dữ liệu, kết xuất mẫu thật, đủ 80 khoản mục dài, thoát chuỗi, trạng thái/cảnh báo, nút in/về/tải lại, dữ liệu cũ/tải lỗi và hợp đồng CSS. Đây là kiểm thử không dùng trình duyệt; **chưa xác minh trực quan ngắt trang A4, hộp thoại in, máy in thật hoặc Vercel**. Build có cảnh báo gói JavaScript vượt 500 KB, không phải lỗi biên dịch.

## Nghiệm thu thủ công tiếp theo

1. Mở hóa đơn đã thanh toán → In hóa đơn: tên/mã đúng, số tiền khớp, không có menu hoặc biểu mẫu thu tiền.
2. Xem trước A4 trên trình duyệt: kiểm tra lề, logo, font tiếng Việt, dữ liệu nhiều trang/80 khoản mục, mô tả dài, tiêu đề bảng, không cắt mất hàng cuối. Kiểm tra cả màn hình điện thoại.
3. Chọn Lưu thành PDF trong cửa sổ in, mở tệp để kiểm tra đủ trang; tắt in nền vẫn đọc rõ bảng/trạng thái. Có thể tắt đầu/chân trang tự động của trình duyệt nếu không muốn in URL/ngày trình duyệt.
4. Kiểm tra chưa thu, thu một phần, miễn phí, đã hủy, tiền mặt thiếu dữ liệu cũ và SePay Test. Không bản nào được khẳng định thu đủ hoặc tiền thật sai trạng thái.
5. Thử đường dẫn hóa đơn khác, dữ liệu lỗi và tài khoản không có quyền; không được in dữ liệu không hợp lệ/không sở hữu.

Nghiệm thu dùng dữ liệu demo, không dùng thông tin bệnh nhân thật. FM-04 sẽ tiếp tục kiểm tra toàn bộ quy trình khám và nghiệm thu trên bản triển khai đã cập nhật.

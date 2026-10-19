# Kiểm tra backend: Lịch làm việc & nghỉ

Ngày kiểm tra: 19/10/2026. Phạm vi: mã nguồn hiện tại, các dịch vụ lịch làm việc/lịch nghỉ và hồi quy liên quan đến đặt lịch; không phải đánh giá toàn bộ backend hoặc bản đang chạy trên Render.

## Kết quả kiểm tra ban đầu

| Bộ kiểm tra | Đạt | Không đạt |
| --- | ---: | ---: |
| `tests/ReceptionSchedules` — lịch làm việc, phân quyền, giao dịch, cập nhật đồng thời, bảo vệ lịch hẹn | 57 | 0 |
| `tests/AdminSchedules` — lịch nghỉ, đổi phạm vi, phân quyền, kiểm tra bác sĩ, xóa và nhật ký | 21 | 4 |
| Tổng | 78 | 4 |

Build Release thành công, không có lỗi biên dịch. Bốn ca không đạt phản ánh ba vấn đề bên dưới; chưa sửa mã nghiệp vụ trong đợt kiểm tra này.

## Cập nhật: đã sửa lỗi 1

Theo yêu cầu chỉ sửa lỗi 1, `UpdateAsync` hiện giữ nguyên giá trị `null` từ quản trị viên để chọn nghỉ toàn phòng khám, thay vì khôi phục mã bác sĩ cũ. Nhánh bác sĩ vẫn giữ cách xác định phạm vi cá nhân như trước. Việc kiểm tra lịch hẹn tiếp tục chạy trong cùng giao dịch, trước khi thay đổi bản ghi.

Đã bổ sung kiểm tra dữ liệu thực sự được lưu, gửi JSON thiếu `doctorId` tương đương `null`, phát hiện lịch hẹn của bác sĩ khác khi mở rộng phạm vi, giữ nguyên dữ liệu/nhật ký khi bị từ chối, đổi ngược sang nghỉ riêng, chuyển giữa hai bác sĩ và cập nhật lịch nghỉ cá nhân.

| Chạy lại sau sửa | Đạt | Không đạt |
| --- | ---: | ---: |
| Toàn bộ `tests/AdminSchedules` sau bổ sung | 30 | 2 |
| `tests/ReceptionSchedules --sql` | 57 | 0 |
| Tổng | 87 | 2 |

Tại thời điểm chỉ sửa lỗi 1, hai ca còn không đạt là BACK-22 và BACK-23, tương ứng lỗi 2 và 3. Chế độ kiểm tra riêng lỗi 1 khi đó loại rõ ràng hai ca này và đạt 30/30. Sau bản sửa lỗi 2 và 3 bên dưới, bộ kiểm tra không còn bỏ qua các ca này.

## Cập nhật mới nhất: đã sửa lỗi 2 và 3

Nhánh quản trị viên trong `ResolveDoctorIdAsync` kiểm tra bác sĩ tồn tại và `IsActive = true` trước khi thêm hoặc cập nhật lịch nghỉ. Mã không tồn tại, mã 0, mã âm và bác sĩ ngừng hoạt động đều bị từ chối bằng `ClinicException` 404 với thông báo **Không tìm thấy bác sĩ đang hoạt động.**, thay vì chờ lỗi ràng buộc SQL. Quyền quản trị được kiểm tra trước khi tra cứu bác sĩ.

`DoctorId = null` vẫn là nghỉ toàn phòng khám và không bị kiểm tra như một mã bác sĩ. Lịch nghỉ cũ của bác sĩ ngừng hoạt động vẫn đọc/xóa được; khi cập nhật phải chuyển sang bác sĩ đang hoạt động hoặc phạm vi toàn phòng khám. Không xóa lịch sử hoặc kích hoạt lại bác sĩ.

Đã bổ sung kiểm tra tạo/cập nhật với các mã bác sĩ không hợp lệ, thông báo trả về, giữ nguyên dữ liệu khi bị từ chối, không sinh bản ghi/nhật ký thành công và xử lý lịch nghỉ cũ của bác sĩ ngừng hoạt động.

| Chạy lại sau sửa cả ba lỗi | Đạt | Không đạt |
| --- | ---: | ---: |
| Toàn bộ `tests/AdminSchedules` | 45 | 0 |
| `tests/ReceptionSchedules --sql` | 57 | 0 |
| Tổng | 102 | 0 |

Build thành công, cảnh báo phụ thuộc bên dưới vẫn còn. Chỉ kiểm thử bằng database SQL Server localhost tạm, đã xóa sau khi chạy; không thay đổi dữ liệu Azure, không cần migration và chưa commit/push/deploy.

## Các vấn đề đã phát hiện

### 1. Đã sửa: đổi lịch nghỉ riêng sang toàn phòng khám không được áp dụng

- Vị trí: `FoMed-API/FoMed.Application/Services/Doctor/DoctorTimeOffService.cs`, phương thức `UpdateAsync`.
- Tái hiện: tạo lịch nghỉ của bác sĩ A; cập nhật cùng bản ghi với `DoctorId = null` để chọn toàn phòng khám.
- Mong đợi: bản ghi được lưu với `DoctorId = null`; kiểm tra xung đột phải xét lịch hẹn của mọi bác sĩ.
- Thực tế trước sửa: vẫn lưu bác sĩ A và trả kết quả thành công. Khi khoảng thời gian mới trùng lịch hẹn của bác sĩ B, yêu cầu vẫn được chấp nhận dưới phạm vi A thay vì được từ chối như yêu cầu nghỉ toàn phòng khám.
- Nguyên nhân: `request.DoctorId ?? timeOff.DoctorId` khôi phục giá trị cũ khi quản trị viên chủ động gửi `null`.
- Tác động: người dùng tưởng đã cho toàn phòng khám nghỉ nhưng lịch nghỉ chỉ áp dụng cho bác sĩ cũ; các bác sĩ khác vẫn có thể nhận lịch.
- Ca tái hiện BACK-13, BACK-14 hiện đã đạt; BACK-26 đến BACK-32 kiểm tra thêm các trường hợp hồi quy.
- Bản sửa: tách cách xử lý phạm vi của quản trị viên và bác sĩ; cho quản trị viên đặt `null` thực sự, rồi kiểm tra xung đột theo phạm vi mới.

### 2. Đã sửa: mã bác sĩ không tồn tại đi xuống lỗi cơ sở dữ liệu

- Vị trí: `DoctorTimeOffService.ResolveDoctorIdAsync`, nhánh quản trị viên.
- Tái hiện: quản trị viên tạo lịch nghỉ với mã bác sĩ không tồn tại.
- Mong đợi: từ chối bằng lỗi nghiệp vụ rõ ràng, tương tự lịch làm việc (404), trước khi lưu.
- Thực tế trước sửa: phát sinh `DbUpdateException`; bản ghi không hợp lệ không được lưu. Bộ xử lý lỗi API không ánh xạ lỗi khóa ngoại sang lỗi nghiệp vụ nên yêu cầu này đi vào nhánh HTTP 500.
- BACK-22, BACK-25 và các kiểm tra mã 0/âm, cập nhật mã không tồn tại hiện đều đạt.
- Bản sửa: kiểm tra sự tồn tại và hoạt động của bác sĩ tại dịch vụ, không dùng lỗi ràng buộc SQL làm kiểm tra đầu vào.

### 3. Đã sửa: thiếu kiểm tra bác sĩ đang hoạt động khi thêm lịch nghỉ

- Tái hiện: tạo lịch nghỉ mới cho bác sĩ có `IsActive = false`.
- Thực tế trước sửa: yêu cầu vẫn được chấp nhận, khác với quy tắc tạo lịch làm việc chỉ dành cho bác sĩ đang hoạt động.
- BACK-23 và các kiểm tra cập nhật sang bác sĩ ngừng hoạt động, đọc/xóa/chuyển phạm vi lịch cũ hiện đều đạt.
- Bản sửa: thống nhất kiểm tra bác sĩ đang hoạt động khi tạo/cập nhật; đọc và xóa lịch cũ vẫn được cho phép, cập nhật phải chuyển sang phạm vi hợp lệ.

## Các luồng đã đạt

- Chặn người không có quyền tạo/xóa qua nhánh quản trị viên; bác sĩ không quản lý lịch nghỉ của người khác hoặc xóa lịch nghỉ toàn phòng khám.
- Chặn khoảng nghỉ không hợp lệ và khoảng nghỉ trùng lịch hẹn theo phạm vi hiện tại; cho phép khoảng liền kề không giao nhau.
- Cập nhật bị từ chối không làm thay đổi dữ liệu; xóa đúng bản ghi, lần xóa tiếp theo trả không tìm thấy và có nhật ký thao tác.
- Lịch làm việc: kiểm tra ngày/giờ/thời lượng, thêm nhiều ngày nguyên tử, chặn trùng ca, bảo vệ lịch hẹn, giữ lịch sử khi ngừng ca, chống ghi đè phiên bản cũ và kiểm thử đặt lịch đồng thời với ngừng ca.

## Cảnh báo phụ thuộc

Build báo NU1902/NU1903 đối với `System.Security.Cryptography.Pkcs` và `System.Security.Cryptography.Xml` phiên bản `10.0.10` trong đồ thị phụ thuộc. Đây là cảnh báo bảo mật từ NuGet, không phải lỗi biên dịch. Chưa đánh giá khả năng khai thác trong FoMed hoặc xác định phiên bản thay thế; không tắt cảnh báo hoặc tự nâng cấp gói trong đợt kiểm tra này.

## Cách chạy lại và giới hạn

Yêu cầu .NET 10, SQL Server localhost và quyền Windows tạo/xóa cơ sở dữ liệu thử nghiệm:

```powershell
dotnet run --project tests/AdminSchedules/AdminSchedules.csproj -c Release
dotnet run --project tests/ReceptionSchedules/ReceptionSchedules.csproj -c Release -- --sql
```

- Kiểm thử gọi dịch vụ thật, giao dịch SQL Server thật và khóa `sp_getapplock`; không dùng InMemory để mô phỏng đường ghi SQL.
- Mỗi thao tác dịch vụ lịch nghỉ dùng DbContext mới để tránh sai lệch do thực thể còn được theo dõi sau rollback.
- Chỉ tạo dữ liệu giả trong database localhost có tên ngẫu nhiên và ràng buộc tên/đích kết nối; database được xóa trong `finally` sau khi chạy.
- Tất cả database tạm của các lần kiểm tra đã được xóa thành công; không đọc cấu hình bí mật và không ghi dữ liệu Azure.
- Đây chủ yếu là kiểm thử tích hợp dịch vụ/SQL, không phải toàn bộ kiểm thử HTTP/JWT hoặc kiểm thử giao diện trình duyệt.
- Bộ lịch nghỉ cố ý trả exit code 1 khi lỗi còn tồn tại, để không báo xanh sai.
- Đã sửa cả ba lỗi lịch nghỉ tại mã nguồn local theo yêu cầu; chưa commit, push hoặc deploy. Cảnh báo phụ thuộc không nằm trong phạm vi bản sửa này.

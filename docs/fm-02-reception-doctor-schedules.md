# FM-02 — Lễ tân quản lý lịch bác sĩ

## Phạm vi đã triển khai

- Lễ tân vào **Lịch bác sĩ** (`/reception/schedules`) để xem ca làm việc hằng tuần, lọc bác sĩ/ngày/trạng thái, phân trang 10 ca.
- Thêm nhiều ngày có cùng giờ và thời lượng mỗi lượt trong một lần; sửa từng ngày; ngừng áp dụng có xác nhận; bật lại bằng sửa ca.
- Xem lịch nghỉ của bác sĩ và lịch nghỉ toàn phòng khám. Lễ tân không được tạo, sửa hoặc xóa lịch nghỉ; quyền này giữ nguyên cho quản trị viên/bác sĩ trên tuyến hiện có.
- Thông báo Sonner sau khi lưu thành công; lỗi tiếng Việt hiển thị trong cửa sổ, khóa gửi trùng, giữ cửa sổ khi thất bại. Cửa sổ có nhãn, xử lý Tab/Escape, trả lại tiêu điểm và khóa cuộn nền.
- Bộ lọc lưu trên URL. Dữ liệu ca cũ không hiện khi đổi bác sĩ, đang tải hoặc tải lỗi. Bộ lọc ngày/trạng thái và phân trang chạy trên toàn danh sách ca đã nhận từ API, không chỉ trang hiện tại.

## Phân quyền và API

Tuyến mới yêu cầu JWT vai trò `Receptionist` hoặc `Admin`, đồng thời dịch vụ kiểm tra tài khoản đang hoạt động và vai trò trong cơ sở dữ liệu. Lễ tân không được mở rộng quyền quản lý tài khoản bác sĩ, chuyên khoa, đơn giá hay người dùng.

| Phương thức | Đường dẫn | Tác dụng |
| --- | --- | --- |
| GET | `/api/reception/schedules?doctorId=...` | Ca làm việc, gồm cả ngừng áp dụng |
| GET | `/api/reception/schedules/doctors` | Danh sách lựa chọn bác sĩ tối thiểu: mã, tên, học hàm/chức danh, trạng thái |
| GET | `/api/reception/schedules/time-off?doctorId=...` | Lịch nghỉ chỉ đọc; luôn bao gồm lịch nghỉ toàn phòng khám |
| POST | `/api/reception/schedules/batch` | Tạo ca theo nhiều ngày, toàn bộ thành công hoặc toàn bộ hủy |
| PUT | `/api/reception/schedules/{id}` | Sửa một ca; bắt buộc `expectedVersion` đã đọc |
| DELETE | `/api/reception/schedules/{id}?expectedVersion=...` | Ngừng áp dụng, **không xóa bản ghi** |

Tuyến `/api/admin/schedules` vẫn chỉ dành cho Admin. Giao diện quản trị mới gửi phiên bản khi sửa/ngừng ca. API quản trị chấp nhận yêu cầu cũ không có phiên bản để tương thích, nhưng nếu có gửi thì phiên bản phải khớp. Phiên bản là dấu vân tay nội dung ca, không phải bộ đếm thay đổi và không phát hiện chuỗi sửa rồi trả lại đúng nội dung cũ.

## Bảo vệ lịch hẹn

- Ngày 0–6, giờ trong cùng ngày, không có giây; mỗi lượt 5–240 phút và phải nằm trọn trong ca.
- Chặn trùng ca đang áp dụng của cùng bác sĩ/ngày; cho phép ca liền kề, không cho phép ca qua đêm.
- Khi sửa/ngừng ca, kiểm tra lịch Pending/Confirmed **từ đầu ngày hôm nay theo giờ Việt Nam** và mọi lượt InProgress, kể cả đang khám từ ngày trước. Lịch Pending/Confirmed cũ trước hôm nay, Completed, Cancelled, NoShow không khóa cấu hình ca.
- Lịch hẹn còn được bảo vệ phải có ca đang áp dụng đúng bác sĩ, thứ, mốc giờ, thời lượng và thời điểm kết thúc. Không tự chuyển giờ, đổi bác sĩ hoặc hủy lịch hẹn.
- Có thể kéo dài ca nếu vẫn giữ các lịch hẹn hợp lệ. Nếu bị chặn, xử lý lịch hẹn/lượt khám theo nghiệp vụ rồi tải lại danh sách trước khi sửa.
- Kiểm tra dùng chung cho sửa/ngừng ca qua lễ tân, quản trị viên và API lịch riêng của bác sĩ. Đặt lịch và thay đổi ca dùng chung khóa giao dịch SQL để tránh đặt vào ca vừa bị ngừng.
- Lịch nghỉ là ngoại lệ theo ngày, không làm thay đổi ca lặp lại hằng tuần. Cơ chế đặt lịch hiện có vẫn ưu tiên loại trừ lịch nghỉ.
- Tạo/sửa/ngừng ca qua dịch vụ quản trị/lễ tân ghi nhật ký trong cùng giao dịch; có mã ca và giá trị trước/sau cho thay đổi. Lịch hẹn và lịch sử không bị ghi lại bởi thao tác cấu hình ca.

## Kiểm chứng

```powershell
dotnet run --project tests/ReceptionSchedules -- --sql
dotnet run --project tests/AppointmentAuthorization
dotnet build FoMed-API/FoMed.Api/FoMed.Api.csproj -c Release --no-restore
```

```powershell
cd FoMed-Frontend
node tests/reception-schedules.audit.cjs
node tests/admin-notifications.audit.cjs
node tests/medicine-catalog.audit.cjs
npm run lint
npm run build
```

Kiểm thử mới: 57 kiểm tra backend và 44 kiểm tra frontend. Backend gồm SQL Server cục bộ thật, dữ liệu giả, ca trùng, hủy toàn bộ nhóm ngày, phân quyền, phiên bản, bảo vệ lịch hẹn kể cả đã quá giờ hôm nay, ca xuất hiện trong giờ đặt khám, ngoại lệ nghỉ và cạnh tranh giữa đặt lịch/ngừng ca trên hai kết nối. Chế độ `--sql` chỉ tạo một cơ sở dữ liệu `FoMed_Schedules_Test_<GUID>` trên `localhost`, kiểm tra tên đích trước khi tạo và xóa bằng `finally`; không đọc cấu hình riêng, không kết nối Azure.

Frontend là kiểm tra biểu mẫu, hợp đồng API, kết xuất trang và thao tác với dữ liệu giả; **không thay cho kiểm thử trình duyệt thật trên Vercel**. Build có cảnh báo kích thước gói JavaScript lớn hơn 500 KB, không phải lỗi biên dịch.

## Triển khai và nghiệm thu còn lại

Không cần thay đổi lược đồ hay chạy SQL migration cho FM-02. Chưa commit, push hoặc triển khai thay đổi này. Khi được phép triển khai, cập nhật API trước rồi frontend.

Trên localhost/bản cloud đã cập nhật, dùng tài khoản lễ tân: tạo ca cho bác sĩ demo, mở đặt lịch để thấy khung mới, đặt lịch demo, thử ngừng/thu hẹp ca và xác nhận bị chặn. Kiểm tra lịch nghỉ toàn phòng khám, tải lại sau khi người khác sửa, rồi kiểm tra tài khoản bệnh nhân không truy cập được trang/API nhân viên. Không sử dụng dữ liệu bệnh nhân thật trong nghiệm thu demo.

Ca hiện tại là lịch tuần không có ngày hiệu lực; chưa bổ sung lịch ca một ngày hoặc khoảng hiệu lực. Sắp xếp lại/hủy hàng loạt lịch hẹn không thuộc FM-02.

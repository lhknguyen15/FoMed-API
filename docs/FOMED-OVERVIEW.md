# FoMed — Hệ thống quản lý phòng khám

FoMed là dự án web hỗ trợ quản lý quy trình phòng khám, từ tìm bác sĩ và đặt lịch
đến tiếp đón, khám bệnh, trả kết quả cận lâm sàng, kê đơn, thu ngân và cấp phát thuốc.
Dự án gồm hai repository độc lập: **FoMed-API** và **FoMed-Frontend**.

Tài liệu này dành cho người xem demo, thành viên dự án và người muốn tìm hiểu mã nguồn.
Bản demo dùng dữ liệu giả để trình diễn; **chưa phải hệ thống vận hành phòng khám thật**.

## 1. Đường dẫn truy cập

| Nội dung | Đường dẫn |
| --- | --- |
| Website FoMed | [Mở website demo](https://fo-med-frontend.vercel.app/) |
| Đăng nhập | [Đăng nhập FoMed](https://fo-med-frontend.vercel.app/login) |
| Đăng ký tài khoản bệnh nhân | [Đăng ký](https://fo-med-frontend.vercel.app/register) |
| Danh sách bác sĩ | [Xem bác sĩ](https://fo-med-frontend.vercel.app/doctors) |
| Danh sách chuyên khoa | [Xem chuyên khoa](https://fo-med-frontend.vercel.app/specialties) |
| Danh sách dịch vụ | [Xem dịch vụ](https://fo-med-frontend.vercel.app/services) |
| GitHub backend | [lhknguyen15/FoMed-API](https://github.com/lhknguyen15/FoMed-API) |
| GitHub frontend | [lhknguyen15/FoMed-Frontend](https://github.com/lhknguyen15/FoMed-Frontend) |
| README backend | [Hướng dẫn FoMed-API](https://github.com/lhknguyen15/FoMed-API#readme) |
| README frontend | [Hướng dẫn FoMed-Frontend](https://github.com/lhknguyen15/FoMed-Frontend#readme) |
| Kiểm tra API đang chạy | [API health](https://fomed-api.onrender.com/health) |

Địa chỉ gốc của các endpoint nghiệp vụ: `https://fomed-api.onrender.com/api`.
Đây không phải trang giao diện cho người dùng. Phần lớn endpoint nghiệp vụ yêu cầu
đăng nhập và quyền phù hợp. `/health` chỉ xác nhận API đang chạy, **không xác nhận
database, dữ liệu hay thanh toán đã hoạt động đầy đủ**.

Có thể xem trang công khai mà không đăng nhập. Đặt lịch và các không gian làm việc
yêu cầu tài khoản đúng vai trò. Không công khai tài khoản hoặc mật khẩu nhân viên;
liên hệ người phụ trách dự án để được cấp tài khoản demo riêng.

## 2. Mục tiêu dự án

- Kết nối các bước nghiệp vụ trong một hệ thống, hạn chế nhập lại thông tin giữa các bộ phận.
- Giúp bệnh nhân tìm bác sĩ, tham khảo dịch vụ, đặt lịch và theo dõi thông tin khám.
- Giúp nhân viên theo dõi lịch hẹn, hàng chờ, bệnh án, chỉ định, đơn thuốc và hóa đơn.
- Giúp quản trị viên quản lý danh mục, người dùng, báo cáo và nhật ký hệ thống.

## 3. Hai phần của FoMed

### FoMed-API

[FoMed-API trên GitHub](https://github.com/lhknguyen15/FoMed-API) là backend,
chịu trách nhiệm xác thực, phân quyền, xử lý nghiệp vụ và lưu dữ liệu.

Các nhóm chức năng gồm tài khoản, lịch hẹn, bệnh án, chỉ định/kết quả, đơn thuốc,
kho thuốc, hóa đơn, thanh toán, báo cáo và quản trị. Quyền xem và thao tác dữ liệu
được kiểm tra tại API, không chỉ dựa vào việc ẩn menu trên giao diện.

Công nghệ chính: **.NET 10, ASP.NET Core Web API, Entity Framework Core 10,
SQL Server/Azure SQL, JWT và BCrypt**. Solution gồm ba project:
`FoMed.Api`, `FoMed.Application` và `FoMed.Infrastructure`.

Backend được đóng gói bằng Docker và triển khai demo trên Render.
Swagger phục vụ phát triển ở môi trường Development; không cung cấp link Swagger
cloud như một tính năng công khai.

### FoMed-Frontend

[FoMed-Frontend trên GitHub](https://github.com/lhknguyen15/FoMed-Frontend) là giao diện
web dành cho khách truy cập, bệnh nhân và nhân viên phòng khám.

Frontend hiển thị dữ liệu từ FoMed-API, tổ chức màn hình theo vai trò, hỗ trợ tìm kiếm,
lọc danh sách, nhập liệu và thông báo kết quả thao tác. Dữ liệu nghiệp vụ nằm ở
database phía backend, không nằm trong bản build frontend.

Công nghệ chính: **React 19, TypeScript, Vite, React Router, Tailwind CSS,
Lucide React và Sonner**. Website demo được triển khai trên Vercel.

### Kết nối các phần

```text
Người dùng → FoMed-Frontend (Vercel) → FoMed-API (Render) → Azure SQL
```

Với thanh toán SePay, thông báo giao dịch gửi trực tiếp tới backend.
Frontend theo dõi trạng thái do API trả về và hiển thị kết quả thanh toán;
không tự xác nhận đã thu chỉ vì người dùng đóng cửa sổ QR.

## 4. Chức năng theo vai trò

| Vai trò | Chức năng chính |
| --- | --- |
| Khách truy cập | Xem trang chủ, bác sĩ, chuyên khoa, dịch vụ và hồ sơ bác sĩ |
| Bệnh nhân | Đăng ký/đăng nhập, đặt hoặc hủy lịch theo điều kiện nghiệp vụ, xem lịch hẹn, hồ sơ khám, hóa đơn và cập nhật hồ sơ cá nhân |
| Lễ tân | Quản lý hồ sơ bệnh nhân, đặt lịch tại quầy, tiếp đón, theo dõi hàng chờ, lập hóa đơn và thu ngân |
| Bác sĩ | Xem hàng chờ, bắt đầu/tiếp tục khám, lưu bệnh án, xem lịch sử khám, chỉ định dịch vụ, xem kết quả, kê đơn và hoàn tất lượt khám |
| Kỹ thuật viên | Xem chỉ định chờ, ghi nhận kết quả, kết luận, khoảng tham chiếu và xem lịch sử kết quả |
| Dược sĩ | Theo dõi tồn kho/lô thuốc, nhập kho và cấp phát theo đơn; ưu tiên lô hết hạn trước theo quy tắc FEFO |
| Quản trị viên | Quản lý bác sĩ, chuyên khoa, dịch vụ, lịch làm việc/nghỉ, người dùng/vai trò, tổng quan, báo cáo và nhật ký |

Các vai trò tài khoản trong hệ thống gồm `Patient`, `Receptionist`, `Doctor`,
`Technician`, `Pharmacist` và `Admin`. Khách truy cập không phải vai trò tài khoản.

### Theo dõi kết quả chỉ định

Bác sĩ có thể xem chỉ định và kết quả tại màn hình khám, chỉ định và kê đơn.
Ba trạng thái hiện có là **Chờ thực hiện**, **Đã có kết quả** và **Đã hủy**;
chưa có trạng thái nhận việc/đang thực hiện riêng cho kỹ thuật viên.

Khi còn chỉ định chờ, bảng kết quả tự cập nhật định kỳ lúc đang xem trang và có
nút cập nhật thủ công. Có thông báo khi quan sát được kết quả mới; việc cập nhật
kết quả không ghi đè bệnh án hoặc đơn thuốc bác sĩ đang nhập.

### Thu ngân và thanh toán

Danh sách hóa đơn đã lập hỗ trợ lọc mã hóa đơn, tên/mã bệnh nhân, trạng thái
thanh toán và khoảng ngày lập. Bộ lọc áp dụng trước phân trang trên toàn bộ dữ liệu,
có tổng số kết quả và giữ bộ lọc khi điều hướng quay lại.

Thu tiền mặt ghi nhận số thu, tiền khách đưa và tiền thừa riêng.
Luồng chuyển khoản SePay hỗ trợ yêu cầu thanh toán, theo dõi trạng thái,
xác nhận từ webhook và chống ghi nhận thu trùng khi thông báo được gửi lại.
**Bản demo dùng chế độ Test, không dùng để chuyển tiền thật.**

## 5. Quy trình phòng khám minh họa

1. Bệnh nhân đặt lịch trên website hoặc được lễ tân đặt lịch tại quầy.
2. Lễ tân tiếp đón và đưa bệnh nhân vào hàng chờ.
3. Bác sĩ bắt đầu khám, ghi bệnh án và chỉ định dịch vụ nếu cần.
4. Kỹ thuật viên thực hiện chỉ định và lưu kết quả để bác sĩ theo dõi.
5. Bác sĩ xem kết quả, kê đơn nếu cần và hoàn tất lượt khám.
6. Lễ tân lập hóa đơn khi lượt khám đã hoàn tất, bệnh án đã chốt và không còn
   chỉ định chờ; ghi nhận thu tiền mặt hoặc thanh toán SePay Test.
7. Dược sĩ cấp phát thuốc theo đơn khi đáp ứng các điều kiện nghiệp vụ.
8. Quản trị viên xem báo cáo và nhật ký hoạt động.

Đây là kịch bản trình diễn tổng quát. Mỗi thao tác vẫn phải đáp ứng điều kiện
nghiệp vụ và quyền tài khoản tương ứng; không phải lượt khám nào cũng có
chỉ định cận lâm sàng hoặc đơn thuốc.

## 6. Giới hạn và lưu ý khi trải nghiệm

- Chỉ dùng dữ liệu giả; không nhập thông tin bệnh nhân thật hoặc tải lên hồ sơ nhạy cảm.
- Không quét QR Test bằng ngân hàng thật để chuyển tiền.
- Tư vấn trực tuyến và tin tức y tế hiện là trang thông báo **chức năng đang phát triển**.
- Quên mật khẩu mới tiếp nhận yêu cầu; chưa hoàn thiện gửi email và đặt lại mật khẩu qua token.
- Xuất PDF hóa đơn và hoàn tiền chưa được hỗ trợ đầy đủ.
- Tệp bệnh án cần kho lưu trữ bền vững; bản Render demo hiện tắt lưu trữ đính kèm local.
- API demo có thể cần thời gian khởi động lại sau khi không hoạt động.
- Một thao tác demo có thể ghi dữ liệu lên Azure; không thử xóa/hủy hoặc thay đổi hồ sơ của người khác.
- Trước khi dùng thực tế cần hoàn thiện và đánh giá bảo mật, sao lưu, lưu trữ,
  quy trình vận hành và nghiệm thu thanh toán Live.

## 7. Dành cho thành viên phát triển

Hướng dẫn cài đặt, cấu hình, chạy local và kiểm thử nằm trong README của từng repository:

- [FoMed-API: chạy backend, chuẩn bị database và triển khai Render/Azure](https://github.com/lhknguyen15/FoMed-API#readme).
- [FoMed-Frontend: chạy giao diện, cấu hình API và triển khai Vercel](https://github.com/lhknguyen15/FoMed-Frontend#readme).

Hai repository độc lập: thay đổi backend cần kiểm thử/triển khai API; thay đổi
giao diện cần build/triển khai frontend. Không đưa mật khẩu, token, chuỗi kết nối,
khóa SePay hoặc dữ liệu bệnh nhân thật vào GitHub hay tài liệu chia sẻ.

# FoMed-API

Xem [tổng quan dự án FoMed, link demo và hai repository](docs/FOMED-OVERVIEW.md).

Backend của **FoMed — hệ thống quản lý phòng khám**, phục vụ đặt lịch, tiếp đón,
khám bệnh, cận lâm sàng, kê đơn, kho thuốc, thu ngân và quản trị. Frontend React
được quản lý trong repository độc lập, không nằm trong solution .NET này.

- Backend: [FoMed-API](https://github.com/lhknguyen15/FoMed-API)
- Frontend: [FoMed-Frontend](https://github.com/lhknguyen15/FoMed-Frontend)
- Website demo: [fo-med-frontend.vercel.app](https://fo-med-frontend.vercel.app/)
- API demo: [fomed-api.onrender.com](https://fomed-api.onrender.com/health)

Bản công khai dùng dữ liệu giả để trình diễn, **không phải hệ thống vận hành
phòng khám thật**. Không nhập thông tin bệnh nhân thật hoặc chuyển tiền thật khi thử demo.

## Tài khoản trải nghiệm demo

[Mở trang đăng nhập FoMed](https://fo-med-frontend.vercel.app/login).

Các tài khoản dưới đây được chủ dự án chủ động công khai để trải nghiệm với dữ liệu
giả. Mật khẩu chung là `Test@123`, chỉ dành cho bản demo, không dùng lại ở tài khoản
cá nhân hoặc môi trường thật. Không cung cấp tài khoản Admin.

| Vai trò | Tên đăng nhập | Mật khẩu |
| --- | --- | --- |
| Bệnh nhân | `patient01` | `Test@123` |
| Lễ tân | `demo202610.lt01` | `Test@123` |
| Bác sĩ | `demo202610.bs01` | `Test@123` |
| Kỹ thuật viên | `demo202610.kt01` | `Test@123` |
| Dược sĩ | `demo202610.ds01` | `Test@123` |

Đã kiểm tra đăng nhập thành công và đúng vai trò trên API demo ngày **14/10/2026**.
Đây là kiểm tra đăng nhập, không phải nghiệm thu toàn bộ quy trình hoặc thanh toán Live.
Chỉ sửa README không tạo tài khoản hay thay đổi mật khẩu trong database.

### Gợi ý thử quy trình

1. Bệnh nhân đặt lịch với bác sĩ liên kết tài khoản `demo202610.bs01`; có thể
   đăng nhập bác sĩ trước để xem tên hồ sơ và chọn đúng bác sĩ khi đặt lịch.
2. Lễ tân xác nhận lịch và tiếp đón bệnh nhân vào hàng chờ.
3. Bác sĩ bắt đầu khám, ghi bệnh án và thêm chỉ định nếu cần.
4. Kỹ thuật viên lưu kết quả; bác sĩ xem kết quả, kê đơn nếu cần và hoàn tất lượt khám.
5. Lễ tân lập hóa đơn và ghi nhận thu tiền mặt **mô phỏng**. SePay chỉ dùng **Test**;
   không quét QR hoặc chuyển tiền bằng tài khoản ngân hàng thật.
6. Dược sĩ cấp thuốc theo đơn khi đáp ứng điều kiện nghiệp vụ và tồn kho hợp lệ.

Đăng xuất trước khi đổi tài khoản. Nếu muốn dùng nhiều vai trò đồng thời, sử dụng
hồ sơ trình duyệt riêng; nhiều tab trong cùng hồ sơ có thể dùng chung phiên đăng nhập.

**Lưu ý:** đây là tài khoản dùng chung có quyền thao tác trên dữ liệu demo.
Không nhập thông tin bệnh nhân thật, không đổi mật khẩu, sửa quyền hoặc phá hủy
dữ liệu của lượt thử khác. Dữ liệu có thể thay đổi do người khác cùng trải nghiệm;
không có cam kết tự khôi phục sau mỗi lượt. API/database có thể cần thời gian khởi
động; nếu đăng nhập gián đoạn, chờ rồi thử lại, không gửi yêu cầu liên tục.

## Chức năng hiện có

| Nhóm | Chức năng |
| --- | --- |
| Tài khoản | Đăng nhập, đăng ký bệnh nhân, JWT/refresh token, đổi mật khẩu, hồ sơ cá nhân và phân quyền theo vai trò |
| Đặt lịch | Danh mục bác sĩ/chuyên khoa/dịch vụ, lịch làm việc, lịch nghỉ, khung giờ khả dụng và quản lý lịch hẹn |
| Lễ tân | Hồ sơ bệnh nhân, đặt lịch tại quầy, tiếp đón, hàng chờ và thu ngân |
| Bác sĩ | Hồ sơ bác sĩ, khám/tiếp tục khám, lịch sử bệnh án, chỉ định dịch vụ và kê đơn |
| Kỹ thuật viên | Tiếp nhận chỉ định cận lâm sàng và lưu kết quả |
| Dược sĩ | Tồn kho, lô thuốc, nhập kho và cấp phát theo FEFO |
| Thanh toán | Hóa đơn, thu tiền mặt/tiền thừa, lịch sử thu và chuyển khoản SePay |
| Quản trị | Bác sĩ, chuyên khoa, dịch vụ, lịch làm việc/nghỉ, người dùng/vai trò, báo cáo và nhật ký hệ thống |

Sáu vai trò: `Admin`, `Receptionist`, `Doctor`, `Technician`, `Pharmacist`, `Patient`.
Quyền truy cập được kiểm tra tại API; việc ẩn menu ở frontend không thay thế phân quyền.

### Giới hạn cần biết

- Quên mật khẩu hiện tiếp nhận yêu cầu và trả thông báo chung; **chưa hoàn thiện
  gửi email, phát hành token và xác nhận mật khẩu mới**. Xem [hợp đồng khôi phục mật khẩu](docs/auth-forgot-password.md).
- SePay có luồng tạo yêu cầu, webhook HMAC, kiểm tra số tiền/nội dung và xử lý gửi lại
  không thu trùng. Bản demo dùng **Test**, không coi đây là nghiệm thu thanh toán Live.
- Tệp bệnh án cần kho lưu trữ riêng bền vững. Cấu hình Render demo hiện tắt
  `ClinicalAttachments__Enabled`; không lưu tệp thật trên filesystem tạm của container.
- `/health` chỉ kiểm tra API đang chạy, không kiểm tra kết nối SQL hoặc thanh toán.
- Swagger chỉ được mở trong môi trường `Development`.

## Công nghệ và cấu trúc

- .NET 10 / ASP.NET Core Web API.
- Entity Framework Core 10, SQL Server / Azure SQL, mô hình database-first.
- JWT Bearer, refresh token và BCrypt.
- Swagger/OpenAPI, Repository + Unit of Work.
- Docker và Render cho bản demo backend.

```text
FoMed-API/
  FoMed.Api/              # Controllers, middleware, DI, cấu hình ứng dụng
  FoMed.Application/      # DTO, dịch vụ nghiệp vụ và kiểm tra quyền
  FoMed.Infrastructure/   # EF Core, models, repositories, JWT, BCrypt, SePay
  FoMed.sln
  Dockerfile
database/                 # Schema, migrations và script dữ liệu
docs/                     # Hợp đồng API, nghiệp vụ và triển khai
tests/                    # Kiểm thử/kiểm tra hồi quy .NET
tools/DatabaseTransfer/   # Công cụ chuyển và đối chiếu database demo
render.yaml               # Mẫu cấu hình Render
```

Solution có ba project trên; không có project `FoMed.Domain`.

## Chạy trên máy

### 1. Chuẩn bị

- .NET SDK 10 và SQL Server tương thích schema của dự án.
- Git; SSMS hoặc công cụ SQL để chuẩn bị database riêng cho phát triển.
- Node.js/npm chỉ cần khi chạy repository frontend.

```powershell
git clone https://github.com/lhknguyen15/FoMed-API.git
cd FoMed-API
dotnet restore FoMed-API/FoMed.sln
dotnet build FoMed-API/FoMed.sln
```

Thư mục `FoMed-API` ở lệnh cuối là thư mục solution **bên trong** repository.

### 2. Chuẩn bị database

Schema và hướng dẫn nằm trong [database](database/README.md). Với database mới/rỗng
cho phát triển, xem `database/fomed-create-database.sql`; với database đang có dữ liệu,
đối chiếu và áp dụng migrations cần thiết theo thứ tự tên file.

**Các seed `fomed-seed-data.sql` và `fomed-seed-data-v2.sql` có lệnh xóa dữ liệu.**
Không chạy lại trên Azure demo đã sử dụng hoặc database có dữ liệu cần giữ.
API không tự tạo database, migrate hoặc seed khi khởi động.

### 3. Cấu hình riêng

Đặt bí mật bằng .NET User Secrets trong môi trường phát triển hoặc biến môi trường
khi triển khai. Không đưa SQL password, JWT key, webhook secret hoặc bản sao database
vào Git. Không sao chép cấu hình bí mật của máy khác.

Các khóa ứng dụng cần dùng:

| Khóa | Mục đích |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | Chuỗi kết nối database phát triển của bạn |
| `Jwt:Key` | Khóa ký riêng, ít nhất 32 ký tự |
| `Jwt:Issuer`, `Jwt:Audience` | Giá trị issuer/audience nhất quán khi tạo và kiểm tra JWT |
| `Cors:AllowedOrigins:0` | Origin frontend chính xác, ví dụ `http://localhost:5174` |
| `SePay:Enabled` | Giữ `false` nếu chưa cấu hình thanh toán |
| `ClinicalAttachments:Enabled` | Chỉ bật khi đã chuẩn bị lưu trữ phù hợp |

Ví dụ đặt origin không chứa bí mật:

```powershell
dotnet user-secrets set "Cors:AllowedOrigins:0" "http://localhost:5174" --project FoMed-API/FoMed.Api
```

Nhập các giá trị bí mật trong môi trường riêng, không gửi vào chat hoặc ảnh chụp.
Trên cloud, dấu `:` được biểu diễn bằng `__`, chẳng hạn `Jwt__Key`.

### 4. Khởi động

```powershell
dotnet run --project FoMed-API/FoMed.Api --launch-profile http
```

- API local: `http://localhost:5068/api`
- Swagger: `http://localhost:5068/swagger`
- Health: `http://localhost:5068/health`

Có thể dùng profile `https` tại `https://localhost:7239` sau khi tin cậy chứng chỉ
phát triển. Frontend mặc định dùng Vite proxy tới profile HTTP ở trên.
Đăng nhập bằng tài khoản của **đúng database đang kết nối**, không công khai mật khẩu demo.

## Kiểm tra

Chạy từ gốc repository. Các project kiểm thử là console runner, không phải bộ
test xUnit chạy bằng `dotnet test`.

```powershell
dotnet build FoMed-API/FoMed.sln -c Release
dotnet run --project tests/AppointmentAuthorization
dotnet run --project tests/DoctorProfiles
dotnet run --project tests/DoctorHistory
dotnet run --project tests/DoctorResume
dotnet run --project tests/Reports
dotnet run --project tests/InvoiceSearch
dotnet run --project tests/ClinicWorkflow -- --hosting-only
```

Các lệnh trên kiểm tra logic/hợp đồng và hosting cục bộ; không chứng minh bản deploy
đã được nghiệm thu. Các chế độ `--sql`, `--http` hoặc `--database` có phạm vi khác,
có thể thực hiện thao tác ghi; đọc source/hướng dẫn trước, chỉ dùng database thử nghiệm.

## Triển khai demo: Render + Azure SQL

Frontend trên Vercel gọi API trên Render; API kết nối Azure SQL. Database local
không tự đồng bộ với Azure.

- Docker Build Context: gốc repository (`.`).
- Dockerfile Path: `FoMed-API/Dockerfile`.
- Health Check Path: `/health`.
- Container bind `0.0.0.0:$PORT`; chạy với `ASPNETCORE_ENVIRONMENT=Production`.
- Chuỗi kết nối Azure đặt ở `ConnectionStrings__DefaultConnection`, dùng TLS và
  `TrustServerCertificate=False`; API dùng tài khoản SQL quyền tối thiểu.
- Cho phép origin frontend chính xác qua `Cors__AllowedOrigins__N`, ví dụ
  `https://fo-med-frontend.vercel.app`, không thêm `/` cuối hoặc wildcard.
- JWT key và các bí mật chỉ đặt trong môi trường triển khai.

`render.yaml` là **mẫu chuẩn bị**, hiện còn trỏ nhánh
`feat/hoan-thien-quy-trinh-phong-kham` và mặc định tắt SePay. Nó không phản ánh mọi
cài đặt đang lưu trên Dashboard. Khi deploy từ `main`, chọn đúng nhánh đã merge;
không mặc định coi việc push là đã deploy.

Xem [hướng dẫn Render/Azure](docs/deploy-render-azure.md) và
[DatabaseTransfer](tools/DatabaseTransfer/README.md). Tài liệu triển khai có ghi nhận
các giai đoạn chuẩn bị cũ; kiểm tra Dashboard để xác định phiên bản và cấu hình thực tế.

## SePay

Đọc [hướng dẫn tích hợp SePay](docs/sepay-integration.md) trước khi bật tính năng.
Webhook gửi đến **backend**, không phải frontend:

```text
https://fomed-api.onrender.com/api/webhooks/sepay
```

Giữ `SePay__Environment=Test`, `SePay__AllowLivePayments=false`, khai báo
`SePay__TestDatabaseName` đúng database chỉ chứa dữ liệu giả. Khi bật SePay cần
cấu hình tài khoản test và HMAC secret phù hợp; bản Test không dùng để chuyển tiền thật.
API Access/API key của SePay không thay thế việc xác thực webhook HMAC hiện tại.

## Tài liệu nghiệp vụ

- [FM-05: giới hạn đăng nhập/đăng ký và lưu ý proxy Render](docs/fm-05-auth-rate-limit.md)
- [Nghiệm thu quy trình FM-04 và kiểm thử cục bộ](docs/fm-04-workflow-acceptance.md)
- [Quy trình phòng khám](docs/clinic-workflow.md)
- [Hồ sơ bác sĩ](docs/doctor-public-profile.md)
- [Thu tiền mặt và lịch sử thu](docs/payment-cash-audit.md)
- [Người dùng và vai trò](docs/vc-22-users-roles.md)
- [Báo cáo](docs/vc-23-reports.md)
- [Nhật ký hệ thống](docs/vc-24-audit-logs.md)

Ngoài mật khẩu của năm tài khoản demo được công khai có chủ đích ở trên, không đưa
thông tin bệnh nhân thật, mật khẩu tài khoản thật/hash, token, BACPAC/BAK hoặc tệp bệnh án
vào repository công khai. Bản demo và kiểm thử hiện có không thay thế đánh giá bảo mật,
sao lưu, lưu trữ và quy trình vận hành trước khi dùng thực tế.

# Deploy FoMed-API lên Render Free + Azure SQL Free Offer

## Trạng thái và phạm vi

Đây là quy trình deploy **demo chứa dữ liệu giả**, không phải môi trường y tế production. Đã chuẩn bị Dockerfile, Blueprint, cấu hình TLS/CORS/health và guard lưu trữ. Ngày 06/10/2026, người dùng đã đăng ký Render/Azure và tạo `FoMedDbDemo` trên SQL server `fomed-sql-demo-nguyen` ở Southeast Asia với Free Offer, Overage billing Disabled. Query editor kết nối thành công, database có 0 bảng nghiệp vụ trước import. **Đã export bản sao local sau xác nhận của người dùng, chưa import Azure, chưa deploy Render, chưa commit hoặc push các thay đổi chuẩn bị này.** Đăng ký, xác minh danh tính/thẻ, chấp nhận điều khoản và chọn subscription do chủ tài khoản thực hiện. Không gửi mật khẩu, OTP, thông tin thẻ hoặc secret vào chat.

Render chạy API và cấp URL HTTPS; Azure SQL lưu dữ liệu. SQL Server local giữ nguyên, không đồng bộ tự động với cloud. Frontend vẫn chạy local, không deploy FoMed-FE. SePay bắt đầu ở Test/disabled.

## 1. Đăng ký và giới hạn chi phí

1. Tạo tài khoản tại [Render](https://dashboard.render.com/register), có thể liên kết GitHub repo API.
2. Tạo tài khoản Azure tại [Azure Free](https://azure.microsoft.com/free/), hoàn tất xác minh trên website chính thức. Kiểm tra subscription thực sự có Azure SQL **Free Offer**.
3. Không chọn dịch vụ trả phí, persistent disk hoặc Dedicated IP. Nếu không thấy Apply Offer, dừng lại, không tạo database theo mức phí mặc định.

Azure SQL Free Offer có hạn mức mỗi tháng 100.000 vCore-seconds, 32 GB data và 32 GB backup/database. Khi tạo, chọn **Auto-pause the database until next month**, không chọn Continue using database for additional charges. Hết hạn mức thì tạm không truy cập được, không chuyển sang tính phí. Budget alert chỉ là cảnh báo, không thay cho lựa chọn này. [Microsoft Free Offer](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer?view=azuresql), [FAQ](https://learn.microsoft.com/en-us/azure/azure-sql/database/free-offer-faq?view=azuresql).

Render Free có idle spin-down/cold start và filesystem tạm; không phải SLA thanh toán production. Lưu lượng/build cũng nằm trong hạn mức của workspace. [Render Free](https://render.com/docs/free).

## 2. Database: sao chép demo hiện có đã được xác nhận

FoMedDb hiện có đã có migration và dữ liệu workflow. Người dùng đã **xác nhận sao chép bản hiện tại** thay vì chạy bộ seed ban đầu và dừng API trong lúc xuất. Đã tạo `deploy-private/FoMedDb-demo.bacpac` cùng manifest SHA-256/số dòng: **30 bảng, 3.362 bản ghi**, số lượng và inventory schema trước/sau export khớp. Hai file được Git ignore; không thay đổi database local. Công cụ độc lập [DatabaseTransfer](../tools/DatabaseTransfer/README.md) đã build và qua 19 kiểm tra guard. Chưa import cloud vì SQL Azure password cần người dùng nhập kín trong terminal.

Chạy từ root repo trong terminal của người dùng, không gửi mật khẩu vào chat:

```powershell
dotnet run --project tools/DatabaseTransfer --no-build -- import
```

Công cụ kiểm tra checksum, đúng Azure database đã có, đích chưa có user-defined object/schema/type, yêu cầu xác nhận `IMPORT-FoMedDbDemo` sau khi người dùng kiểm tra Free/Overage Disabled trên portal. Sau nhập, đối chiếu số dòng từng bảng và inventory schema; không reset/ghi đè DB, không truyền SKU mới. Nếu nhập dở/lỗi thì dừng để kiểm tra, không retry bằng seed hoặc xóa database.

Cập nhật chẩn đoán: người dùng đã đăng nhập SQL bằng công cụ; import bị chặn do đích có user-defined objects, verify mismatch. Chưa xác định dữ liệu nhập dở hay khác biệt metadata; không coi đây là deploy thành công và không reset DB. Đã bổ sung `diagnose` chỉ đọc, in chênh lệch số dòng/schema và báo cáo metadata riêng (bản mới qua 25 guard checks):

```powershell
dotnet run --project tools/DatabaseTransfer --no-build -- diagnose
```

Kết quả chẩn đoán tiếp theo: Azure có 30 bảng/3.362 dòng/423 inventory entries và số dòng từng bảng khớp bản xuất. Kiểm tra đủ 39 cặp differences trong báo cáo riêng xác nhận chỉ khác tên constraint: 8 CHECK, 4 FK và 27 index entries. Đã sửa verifier để nhận diện tên tự sinh bằng `is_system_named`, so sánh nhóm constraint/index theo cấu trúc, vẫn kiểm tra tên tường minh và các thuộc tính khác. Bản sửa build 0 warning/error và qua 44 guard checks. Không thay dữ liệu/schema, không ghi đè backup/manifest. Người dùng cần chạy lại `verify` trên Azure bằng bản sửa để xác nhận trạng thái live; chưa nghiệm thu Render/API workflow.

- Chỉ sao chép sau khi xác nhận toàn bộ dữ liệu là demo, sao lưu và tạm dừng thao tác ghi để có snapshot nhất quán.
- Dùng SSMS Export Data-tier Application/BACPAC, lưu ngoài Git hoặc trong `deploy-private/` đã ignore. BACPAC chứa dữ liệu tài khoản và có thể chứa refresh token; không upload lên Git/chat hay dịch vụ công khai.
- **Tạo database rỗng với Free Offer trước**, rồi dùng DacFx/SqlPackage import vào đúng database mới/rỗng đó. Không dùng wizard tạo database mới với SKU mặc định hoặc lựa chọn Premium/General Purpose trả phí. Kiểm tra rằng công cụ không thay đổi SKU/free allowance; nếu không bảo đảm thì dừng để dùng schema/data import có kiểm soát.
- BACPAC không chứa file PDF/ảnh trong App_Data. Không coi metadata trong SQL là đã chuyển file.
- Azure SQL Database không dùng trực tiếp quy trình restore `.bak` của SQL Server local. Các SQL script có `CREATE DATABASE`/`USE FoMedDb` không được chạy nguyên trạng vào DB cloud. Không chạy seed có DELETE trên bản sao đã import.
- Sau import, kiểm tra tables/indexes/checks của SePay/cash/pharmacy/attachment, số lượng bản ghi và dữ liệu tiếng Việt. Không tự migrate/reset trong startup container.
- Đặt JWT key cloud riêng; trước công khai cần đổi mật khẩu tài khoản demo mặc định và vô hiệu phiên refresh copy từ local trên **cloud** với phạm vi đã xác nhận. Không xóa ledger SePay/payments hoặc reset dữ liệu local.

SqlPackage Import hỗ trợ database mới hoặc rỗng; quy trình BACPAC và target phải được kiểm tra theo [Microsoft Import](https://learn.microsoft.com/en-us/azure/azure-sql/database/database-import?view=azuresql) và [SqlPackage Import](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-import?view=sql-server-ver17). Chưa nghiệm thu import vào Free Offer thực tế trong lượt chuẩn bị này.

## 3. Azure SQL server và firewall

Tạo SQL logical server với hostname `<server>.database.windows.net`, region gần Render Singapore nếu Free Offer hỗ trợ. Thiết lập tài khoản quản trị bí mật qua Azure. API nên dùng contained user riêng quyền tối thiểu sau import; quyền schema migration do tài khoản quản trị thực hiện, không gán owner cho API chỉ để tiện.

Azure firewall chỉ cho IP máy bạn lúc import và **các outbound IP range của dịch vụ Render**. Lấy ranges tại Render service → Connect/Outbound; không đoán một IP cố định và không mở tất cả Internet. Dịch vụ có thể dùng mọi IP trong những range được cấp. Nếu Azure firewall yêu cầu start/end thì chuyển CIDR thành phạm vi tương ứng, không thu hẹp thành một IP. [Render Outbound IP](https://render.com/docs/outbound-ip-addresses).

Connection string nhập **trực tiếp vào Environment của Render**, không vào file tracked:

```text
Server=tcp:<server>.database.windows.net,1433;Database=FoMedDbDemo;User ID=<api-user>;Password=<secret>;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;
```

Placeholder không được dùng nguyên trạng. API trên Render từ chối connection local/system DB, TLS không mã hóa, bỏ kiểm tra certificate hoặc thiếu SQL credentials. Chưa có managed identity giữa Render/Azure. Azure serverless cold start có thể gây lỗi lần đầu; webhook retry được idempotent, không tự retry mọi POST thu tiền hoặc bật EF execution strategy tùy tiện vì workflow có transaction explicit.

## 4. Đưa code lên Git — cần hỏi trước khi push

`render.yaml` trỏ branch `feat/hoan-thien-quy-trinh-phong-kham`, Dockerfile `FoMed-API/Dockerfile`, context root. Kiểm tra branch thật trước deploy. Render chỉ lấy **code đã có trên remote**; code local chưa commit/push sẽ không được deploy. Cần review diff, kiểm tra secret rồi xin phép commit/push theo yêu cầu của bạn.

`.dockerignore` chỉ đưa source API vào context, loại Development settings, App_Data, bin/obj, secret/key. Đây không thay thế việc xử lý secret **đã tracked** trong Git: `appsettings.Development.json` hiện là file tracked và đang thay đổi, không stage/push file riêng đó. Nếu từng công khai credential trong lịch sử Git, phải rotate trước dùng cloud; ignore không xóa lịch sử.

Không tự untrack/reset/commit file của người dùng. Các file `.local.json`, BACPAC/BAK và `deploy-private/` bị ignore. Không build image bằng copy cả repository mà bỏ Dockerignore.

## 5. Tạo Render service

Dùng New → Blueprint với `render.yaml`, hoặc New → Web Service:

| Trường | Giá trị |
| --- | --- |
| Runtime | Docker |
| Plan | **Free**, không để plan mặc định |
| Branch | feat/hoan-thien-quy-trinh-phong-kham, sau xác nhận push |
| Region | Singapore |
| Dockerfile | FoMed-API/Dockerfile |
| Docker context | root repo, không đặt Root Directory thành FoMed-API |
| Health check | /health |
| Auto deploy | Off, deploy thủ công có chủ ý |

Blueprint không tạo Postgres/paid disk, tự sinh JWT key riêng và yêu cầu nhập các giá trị `sync:false`. Nếu tạo thủ công, tự sinh JWT key ngẫu nhiên ít nhất 32 ký tự ngay trên máy/secret manager, không dùng key local.

| Environment variable | Giá trị |
| --- | --- |
| `ASPNETCORE_ENVIRONMENT` | Production |
| `ConnectionStrings__DefaultConnection` | connection string Azure ở mục 3, secret |
| `Jwt__Key` | key cloud ngẫu nhiên, secret |
| `Jwt__Issuer` | FoMed.Api.CloudDemo |
| `Jwt__Audience` | FoMed.Frontend.CloudDemo |
| `Cors__AllowedOrigins__0` | http://localhost:5174, hoặc origin frontend chính xác |
| `ClinicalAttachments__Enabled` | false cho Render Free hiện tại |
| `SePay__Enabled` | false lúc deploy lần đầu |
| `SePay__Environment` | Test |
| `SePay__AllowLivePayments` | false |
| `SePay__TestDatabaseName` | FoMedDbDemo, đúng tên DB đã xác nhận dữ liệu demo |

Container chạy user `app`, không root, bind `0.0.0.0:$PORT` (mặc định 10000). Render xử lý HTTPS/HTTP redirect ở edge; ứng dụng không tin header proxy do caller tự gửi. Production không bật Swagger. `/health` chỉ trả liveness, **không chứng minh SQL/login/SePay hoạt động** và không truy vấn SQL theo mỗi lần health polling. [Render Web Services](https://render.com/docs/web-services), [Blueprint](https://render.com/docs/blueprint-spec).

## 6. Frontend local và nghiệm thu

Sau deploy thành công, dùng URL Render thật cho `VITE_API_PROXY_TARGET` trong `.env.local` frontend và giữ `VITE_API_URL=/api`, rồi khởi động lại Vite. Đăng xuất phiên local trước đăng nhập cloud. Không đưa password/JWT key/webhook secret vào VITE.

Kiểm tra public HTTPS `/health`=200, `/swagger`=404, API JWT bảo vệ=401 khi không token; rồi specialties/doctors=200 có dữ liệu và login với account demo đã đổi mật khẩu. Thử workflow riêng trên cloud để xác nhận không thay dữ liệu local. Allowlist CORS đúng origin nếu frontend gọi backend trực tiếp; CORS không thay authentication.

**Giới hạn tệp:** với blueprint hiện tại, upload/download kho local bị 503 có thông báo cần storage bền vững. Không nhận upload rồi âm thầm làm mất file. Chỉ bật lại sau khi chọn private object storage/volume bền vững, migrate file và kiểm thử quyền bệnh án. Không tự tạo storage trả phí trong lượt này.

## 7. Bật SePay sau nghiệm thu backend/database

Chỉ sau khi dữ liệu cloud, credentials, firewall và nghiệp vụ đã đạt, điền BankCode/Gateway/account test và HMAC secret vào Render env. Tạo webhook trên SePay Test mode với URL:

```text
https://<service-thuc-te>.onrender.com/api/webhooks/sepay
```

Đặt Enabled=true, Environment=Test, AllowLivePayments=false và TestDatabaseName đúng. Tiền vào/JSON/gửi lại/HMAC; dùng Mô phỏng giao dịch với code/amount từ FoMed, không chuyển tiền thật. SePay request Test có thể ghi vào báo cáo demo; không bật cùng account/webhook cho local và cloud để tránh phân bổ nhầm. Render/Azure idle pause có thể làm webhook lỗi/timeout; ACK chỉ sau commit, kiểm tra retries và không double payment. Webhook gửi trực tiếp tới URL HTTPS của FoMed-API trên Render.

## 8. Kiểm thử local trước push

```powershell
dotnet publish FoMed-API/FoMed.Api/FoMed.Api.csproj -c Release -o FoMed-API/FoMed.Api/bin/CloudPublishAudit /p:UseAppHost=false
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj -o tests/ClinicWorkflow/bin/HttpAudit
dotnet tests/ClinicWorkflow/bin/HttpAudit/ClinicWorkflow.dll --hosting-only
docker build --file FoMed-API/Dockerfile --tag fomed-api:demo .
```

Hosting audit chỉ loopback, SQL hostname/credentials giả không kết nối DB; kiểm tra startup guard, liveness, Production Swagger/auth/CORS và chặn file trước ghi. Docker build không push image hay tạo cloud resource. Các kết quả local không phải deploy cloud thành công.

Kết quả kiểm thử chuẩn bị 06/10/2026: **31 hosting configuration/Production HTTP**, **129 SePay HTTP/SQL**, 31 HMAC/configuration và 7 time/slot đạt. Docker image Linux build đạt, chạy user 1654 (non-root), không chứa Development settings/App_Data/local secrets; container loopback kiểm tra PORT tùy chỉnh, health=200, Swagger=404, route bảo vệ=401 rồi đã tự dọn. Bản publish cũng loại file Development. Sau đó đã tạo Azure DB qua portal, Query editor đăng nhập được và kiểm tra TCP 1433 từ máy đạt; DatabaseTransfer có 19 guard checks và đã export local. **Chưa kiểm thử import/SQL login bằng công cụ, Render hoặc SePay cloud.**

Đã override dependency `System.Security.Cryptography.Xml` từ bản transitive 9.0.0 lên **10.0.10**, theo [Microsoft security advisory](https://github.com/advisories/GHSA-23rf-6693-g89p); build/publish hiện 0 warning và NuGet vulnerability audit không báo package dễ bị tấn công của API tại thời điểm kiểm tra. Điều này không thay thế kiểm tra bảo mật endpoint/mật khẩu/storage trước public deploy.

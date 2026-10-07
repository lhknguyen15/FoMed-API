# FM-05 — Giới hạn đăng nhập và đăng ký

Ngày thực hiện: 07/10/2026. **Hoàn thành giai đoạn 1 ở code và kiểm thử cục bộ;
chưa triển khai/nghiệm thu trên Render/Vercel.**

## Phạm vi và hạn mức mặc định

Dùng middleware rate limiting tích hợp trong ASP.NET Core, không thêm package hay
migration. Chỉ hai endpoint dưới đây có chính sách; không giới hạn toàn bộ API.

| Endpoint | Theo IP khách đã xác minh | Hạn mức chung mỗi tiến trình API |
| --- | --- | --- |
| `POST /api/auth/login` | 10 yêu cầu / 60 giây | 200 yêu cầu / 60 giây |
| `POST /api/auth/register` | 5 yêu cầu / 600 giây | 50 yêu cầu / 60 giây |

Các yêu cầu thành công, sai thông tin và không hợp lệ đều tiêu thụ hạn mức.
Hạn mức chung chạy trước hạn mức IP: một IP đã bị chặn vẫn tiêu thụ hạn mức chung.
Hai endpoint có bộ đếm riêng. Dùng cửa sổ cố định, không xếp hàng yêu cầu; vượt mức
trả ngay HTTP 429 trước khi chạy thao tác đăng nhập/tạo tài khoản.

Phản hồi có `Retry-After` (số giây), `Cache-Control: no-store`, `statusCode: 429`,
`retryAfterSeconds` và thông báo tiếng Việt có dấu. CORS chỉ cho origin đã cấu hình
và cho phép frontend đọc `Retry-After`. Không đưa mật khẩu, JWT, IP hoặc lỗi nội bộ
vào thông báo giới hạn.

Các API khám bệnh, thu ngân, cấp thuốc, webhook SePay, refresh token, quên mật khẩu
và `/health` **không được gắn hạn mức mới trong giai đoạn này**. HTTP 401/403 và
phân quyền cũ vẫn giữ nguyên. Giới hạn nghiệp vụ theo tài khoản là giai đoạn sau.

## Nhận diện IP sau proxy — đặc biệt trên Render

`AuthRateLimit:ClientIpSource` mặc định là `Auto`:

- Local Development/Audit, không phải Render: dùng IP kết nối trực tiếp, bỏ qua
  mọi header nhận diện do người gọi tự gửi.
- Render hoặc Production chưa xác minh proxy: dùng **hạn mức chung**, chưa bật
  hạn mức thấp theo IP khách. Tránh gom mọi người dùng vào IP của proxy.
- Có danh sách `TrustedProxyIps` đã được quản trị xác minh: dùng chế độ
  `TrustedProxy`, xử lý đúng một hop `X-Forwarded-For` bằng middleware chính thức.
  Proxy phải kiểm soát địa chỉ cuối header; không lấy địa chỉ đầu do khách gửi.
  Peer không tin cậy hoặc header thiếu/sai sẽ dùng hạn mức chung, không tự nhận IP.

**Chưa xác minh được IP ingress proxy của dịch vụ Render này. Không tuyên bố đã
bật bảo vệ theo từng IP trên cloud.** IP outbound Render dùng để mở firewall Azure
SQL không phải bằng chứng nhận diện ingress proxy; không sao chép vào danh sách tin cậy.
Không đặt `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`: API từ chối khởi động với
cấu hình tự động không giới hạn proxy đó. Chế độ `Connection` cũng bị từ chối trên Render.

Đọc [hướng dẫn proxy của Microsoft](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
và [IP outbound Render](https://render.com/docs/outbound-ip-addresses) trước khi thay đổi cấu hình.

## Cấu hình và vận hành

Cấu hình công khai nằm ở `FoMed-API/FoMed.Api/appsettings.json`. Có thể ghi đè bằng
biến môi trường, ví dụ `AuthRateLimit__Login__PermitLimit`,
`AuthRateLimit__Login__WindowSeconds`, `AuthRateLimit__Login__ServicePermitLimit`,
`AuthRateLimit__Login__ServiceWindowSeconds`; nhóm `Register` tương tự.

`ClientIpSource` nhận `Auto`, `Connection`, `TrustedProxy`, `Service`.
Chỉ dùng `TrustedProxy` sau khi xác minh topology và khai báo từng IP chính xác qua
`AuthRateLimit__TrustedProxyIps__0`, `__1`, …; không dùng hostname, CIDR hoặc IP bất kỳ.
Permit phải trong 1–10000, thời gian trong 1–86400 giây. Cấu hình không hợp lệ bị từ
chối lúc khởi động. `AuthRateLimit__Enabled=false` tắt cả hai lớp giới hạn, chỉ dùng
khi có quyết định vận hành rõ ràng. Thay đổi cấu hình cần khởi động lại API.

Giữ `Auto` trên Render hiện tại: sau khi triển khai code mới, hạn mức chung hoạt
động mà không cần thêm biến môi trường; log khởi động báo hạn mức IP chưa hoạt động.
Chưa thay đổi cấu hình hoặc dữ liệu cloud trong lượt thực hiện này.

Bộ đếm nằm trong bộ nhớ, mất khi restart; nhiều instance có bộ đếm độc lập.
Các người dùng chung IP/NAT chia sẻ hạn mức IP. Cửa sổ cố định có thể có burst ở
ranh giới. Đây không thay thế chống DDoS/WAF, chống dò mật khẩu theo tài khoản,
giới hạn đồng thời hoặc bộ đếm phân tán. Không tự khóa tài khoản theo username
người gọi nhập, tránh tạo cách khóa tài khoản của người khác.

## Frontend

Login/register giữ dữ liệu đang nhập khi nhận 429, hiển thị thông báo Sonner một
lần và thông báo tại form, khóa nút gửi với đếm ngược. Vẫn cho sửa dữ liệu trong
thời gian chờ. Nếu metadata thiếu/sai, dùng thời gian chờ giao diện 60 giây.
Không refresh JWT hay tự gửi lại POST sau 429 hoặc khi hết thời gian chờ.
Chặn gửi đồng thời ở handler, không chỉ bằng thuộc tính disabled của nút.

## Kiểm thử ngày 07/10/2026

- 93 kiểm tra cấu hình/proxy/middleware loopback đạt: giả IP, peer lạ, thiếu/sai
  header, 20 yêu cầu đồng thời chỉ 2 được xử lý, phục hồi hạn mức, tách chính sách.
  Đây là host handler giả, không phải đăng nhập thật hay cloud.
- 12 kiểm tra API/HTTP/SQL đạt trên database localhost dùng một lần: đăng nhập,
  đăng ký, phản hồi 429/CORS, không tạo tài khoản khi bị chặn, chờ rồi đăng nhập lại.
- Frontend: 45 kiểm tra React/HTTP tổng hợp mới và 264 kiểm tra hồi quy đạt.
- Hồi quy API: 321 quy trình, 129 SePay mô phỏng, 51 SQL đạt; build API/runner,
  frontend lint/TypeScript/Vite đạt. Còn cảnh báo bundle JavaScript trên 500 KB.

Các số là số kiểm tra/assertion, không cộng thành tình huống độc lập. Không kiểm
thử trực quan trình duyệt trong lượt này. Database/kho tệp tạm được dọn sau lượt
chạy; không truy cập hoặc ghi FoMedDb/Azure, không gọi SePay hay chuyển tiền thật.

Chạy ở root FoMed, theo điều kiện an toàn của [FM-04](fm-04-workflow-acceptance.md):

```powershell
dotnet publish FoMed-API/FoMed.Api/FoMed.Api.csproj -c Release --no-restore -o FoMed-API/FoMed.Api/bin/WorkflowAuditPublish
dotnet build tests/ClinicWorkflow/ClinicWorkflow.csproj -c Release --no-restore
dotnet tests/ClinicWorkflow/bin/Release/net10.0/ClinicWorkflow.dll --rate-limit-only
dotnet tests/ClinicWorkflow/bin/Release/net10.0/ClinicWorkflow.dll --http --rate-limit-only
```

Lệnh `--rate-limit-only` kiểm tra middleware không cần SQL; thêm `--http` cần SQL Server localhost,
Windows Integrated Security và quyền tạo/dọn database kiểm thử ngẫu nhiên. Runner
từ chối publish chứa cấu hình Development riêng. Báo cáo HTTP ignored:
`tests/ClinicWorkflow/bin/audit-results/http-auth-rate-limit.json`; chỉ nghiệm thu
khi exit code 0, `completed=true`, `failed=0`.

Frontend: chạy `node tests/auth-rate-limit.audit.cjs` trong repo FoMed-Frontend,
rồi `npm run lint` và `npm run build`.

## Còn lại trước khi nghiệm thu cloud

- Commit/merge/deploy khi người dùng yêu cầu; kiểm tra 429/CORS và giao diện bằng
  tài khoản demo, trên môi trường kiểm thử có hạn mức nhỏ, không flood production.
- Xác minh ingress proxy Render trước khi bật hạn mức theo IP khách.
- Thiết kế riêng hạn mức refresh/quên mật khẩu và API nghiệp vụ theo tài khoản;
  giữ polling khám/SePay và retry thanh toán an toàn, không áp chung một quota thấp.

Tham khảo [rate limiting ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit-samples?view=aspnetcore-10.0).

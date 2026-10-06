# Chuyển bản sao FoMedDb demo lên Azure

Công cụ độc lập, không nằm trong startup API. Chỉ xuất từ `FoMedDb` trên SQL Server local và nhập vào database **đã có sẵn** `FoMedDbDemo` tại `fomed-sql-demo-nguyen.database.windows.net`. Không reset/xóa database, không chạy seed, không thay connection string API, không nhận mật khẩu qua tham số dòng lệnh.

## Trạng thái ngày 06/10/2026

- Người dùng xác nhận sao chép dữ liệu demo và dừng API trong lúc xuất.
- Đã xuất `deploy-private/FoMedDb-demo.bacpac`: **30 bảng, 3.362 bản ghi**; số lượng bản ghi và inventory schema trước/sau xuất khớp nhau.
- Có manifest riêng kèm SHA-256 của BACPAC, tên đích, thời điểm và số lượng bản ghi từng bảng; không chứa connection string/mật khẩu. Cả hai file được Git ignore.
- Build 0 warning/error; bản sửa đối chiếu qua **44 kiểm tra guard**. Người dùng đã đăng nhập SQL thành công từ công cụ. Báo cáo chẩn đoán Azure có **30 bảng, 3.362 bản ghi, 423 inventory entries**, số dòng từng bảng khớp bản xuất. Phân tích đủ 39 cặp raw differences: **8 CHECK, 4 FK, 27 entries của index khóa**, chỉ khác tên constraint; các trường cấu trúc còn lại khớp. Bản cũ báo mismatch giả vì hash bao gồm tên constraint SQL tự sinh. **Chưa chạy lại verify live bằng bản sửa; chưa nghiệm thu API/Render.** Không nhập lại hoặc reset database.

## Nhập vào Azure — người dùng nhập mật khẩu trong terminal

Chạy từ `C:\Users\M\Desktop\FoMed` trong terminal riêng của VS Code:

```powershell
dotnet run --project tools/DatabaseTransfer --no-build -- import
```

1. Login: nhập `fomedadmin` hoặc Enter để dùng tên mặc định này.
2. Nhập mật khẩu SQL Azure trực tiếp khi xuất hiện `Azure SQL password (hidden)`. Không hiện ký tự khi gõ; Enter để tiếp tục. Không gửi secret lên chat, không nhập vào lệnh/shell history, không bật transcript.
3. Công cụ kiểm tra checksum, Azure engine, đúng tên database, database chưa có object/schema/type nghiệp vụ và đọc service objective hiện tại.
4. Kiểm tra lại Azure portal vẫn **Free / Overage billing Disabled**. Chỉ khi đúng, nhập `IMPORT-FoMedDbDemo` để đồng ý nhập bản sao. Công cụ không truyền thiết lập SKU hay kích thước database vào DacFx; tài khoản quản trị vẫn có quyền lớn, nên không xóa/tạo lại database trong lúc chạy.
5. Chờ dòng `PASS` đối chiếu số bản ghi từng bảng và inventory cột/index/FK/check constraint. Tên SQL tự sinh được nhận diện bằng metadata `is_system_named` của cả hai phía, không đoán bằng regex. So sánh nguyên nhóm constraint/index, giữ thứ tự khóa, grouping và số lượng trùng lặp; tên do người dùng đặt vẫn so sánh chính xác. Kiểm tra Free Offer và Overage billing trên portal sau import. So sánh inventory/số lượng không phải so sánh từng giá trị dữ liệu, chưa bao phủ mọi object hoặc workflow.

Không có SQL credentials được lưu ra file. Nếu import lỗi, có thể có dữ liệu/schema nhập dở: **không chạy seed hoặc xóa database để thử lại**. Công cụ chặn import vào đích không rỗng. Gửi loại lỗi/mã SQL hoặc ảnh không có mật khẩu để chẩn đoán; exception thô được ẩn vì có thể chứa dữ liệu hoặc secret.

Nếu bị chặn firewall, thêm đúng IP máy đang chạy lệnh, không mở toàn Internet. Query editor có thể thấy IP khác với kết nối SQL từ máy do đường mạng/proxy; cần đọc lỗi kết nối thực tế, không mở rộng dải IP để đoán.

## Kiểm tra lại và xuất lại

```powershell
dotnet run --project tools/DatabaseTransfer --no-build -- verify
dotnet run --project tools/DatabaseTransfer --no-build -- diagnose
dotnet run --project tools/DatabaseTransfer --no-build -- inspect
dotnet run --project tools/DatabaseTransfer --no-build -- --self-test
```

`verify` chỉ đọc Azure và cần nhập mật khẩu kín. Nó in tổng số bảng/bản ghi thực tế và các dòng `DIFF` nêu bảng thiếu/thừa, số dòng khác nhau hoặc cấu trúc schema khác. Chỉ chuẩn hóa tên constraint khi SQL xác nhận `is_system_named`; các thuộc tính khác vẫn giữ nguyên. Manifest cũ chưa có đủ metadata sẽ được bổ sung **trong bộ nhớ** từ schema local chỉ khi raw hash schema hiện tại khớp raw hash bản xuất; số dòng baseline vẫn dùng bản xuất, không dùng dữ liệu local hiện tại. Không sửa BACPAC/manifest gốc. Nếu thiếu baseline đáng tin, phép kiểm tra vẫn từ chối thay vì bỏ qua lỗi.

`diagnose` cũng chỉ đọc: thêm thống kê loại object, 15 tên object đầu, schema/type nghiệp vụ và raw inventory differences. Raw tên tự sinh khác nhau có thể chỉ là thông tin chẩn đoán, không phải lỗi cấu trúc. Tên object được in, nhưng không in định nghĩa constraint, dữ liệu bệnh nhân, password/token hay connection string. Chi tiết metadata được lưu riêng trong `deploy-private/Azure-diagnose-*.local.json` (Git ignore); không chia sẻ nội dung đó khi chưa kiểm tra. `inspect` chỉ đọc local, không in thông tin người dùng/bệnh nhân. `--self-test` không kết nối SQL.

Khi import bị chặn hoặc verify mismatch, chạy `diagnose`, gửi các dòng tổng hợp `Azure:`, `Export:`, `DIFF:`, `OBJECT-COUNT:`, `EXPORT-ONLY:`/`AZURE-ONLY:` để xác định nguyên nhân trước khi sửa. Không tự xóa/nhập lại database. Mật khẩu ẩn cần nhập rồi Enter; chuỗi `IMPORT-FoMedDbDemo` chỉ nhập khi công cụ đang yêu cầu, không phải lệnh PowerShell.

Nếu chưa có bản xuất, dừng mọi ứng dụng/test đang ghi vào FoMedDb, rồi chạy:

```powershell
dotnet run --project tools/DatabaseTransfer --no-build -- export
```

Nhập `WRITES-PAUSED` sau khi chắc chắn không còn thao tác ghi. DacFx export cần nguồn không bị thay đổi trong suốt quá trình để đảm bảo snapshot; kiểm tra số lượng trước/sau không phát hiện được mọi thay đổi cùng số dòng. Công cụ từ chối khi FoMed.Api còn chạy hoặc backup đã tồn tại, không tự dừng process và không ghi đè bản sao.

## Giới hạn và bảo mật

- BACPAC chứa dữ liệu tài khoản, password hash và có thể có token/ledger: giữ riêng, không commit/upload công khai. File đính kèm trên ổ đĩa **không được chuyển theo**.
- Không thay/mất dữ liệu local. Cloud là bản sao độc lập, không tự đồng bộ.
- Trước công khai API cần JWT key riêng, xử lý tài khoản demo/phiên refresh cloud và tạo SQL user ít quyền cho API ở bước tiếp theo; chưa thực hiện các thay đổi này.
- Sau import, chưa được coi là Render/SePay hoạt động; vẫn phải deploy và kiểm thử cloud. SePay chỉ dùng Test mode.
- DacFx chính thức: [NuGet](https://www.nuget.org/packages/Microsoft.SqlServer.DacFx/170.5.96), [Export](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-export?view=sql-server-ver17), [Import vào database mới/rỗng](https://learn.microsoft.com/en-us/sql/tools/sqlpackage/sqlpackage-import?view=sql-server-ver17).
- `is_system_named` chính thức: [CHECK](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-check-constraints-transact-sql?view=sql-server-ver17), [FOREIGN KEY](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-foreign-keys-transact-sql?view=sql-server-ver17), [PRIMARY/UNIQUE KEY](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-key-constraints-transact-sql?view=sql-server-ver17).

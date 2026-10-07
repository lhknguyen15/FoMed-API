# FM 01 Quản lý danh mục thuốc

Đã triển khai danh mục thuốc cho **Admin** ở API và frontend. Đây là thay đổi mã
nguồn local, không xác nhận đã deploy lên Render/Vercel hoặc chạy nghiệm thu Azure.

## Chức năng

- Menu **Quản trị → Danh mục thuốc**, route `/admin/medicines`.
- Thêm/sửa tên, đơn vị, giá bán và mô tả. Thuốc mới đang sử dụng, tồn kho bằng 0
  cho đến khi nhập lô qua nghiệp vụ kho hiện có.
- Tìm theo tên/đơn vị/mô tả và lọc đang/ngừng sử dụng trên **toàn bộ danh mục**.
  Phân trang server 20 thuốc, tổng số kết quả và thứ tự tên/ID ổn định.
- Ngừng/sử dụng lại qua hộp thoại xác nhận; không có API xóa cứng thuốc.
- Ngừng sử dụng bị chặn nếu thuốc còn trong đơn của ca đang khám, hoặc đơn của
  ca hoàn tất chưa cấp phát đủ. Phân bổ từng lô và trường `BatchId` của dữ liệu
  cấp phát cũ đều được tính. Đơn thuộc ca đã hủy/không đến không chặn.
- Tồn kho gồm các lô, kể cả hết hạn. Tồn có thể cấp chỉ tính lô còn hạn của thuốc
  đang sử dụng, không phải số lượng đã giữ chỗ cho các đơn chưa cấp.
- Tên và đơn vị bắt buộc; giới hạn lần lượt 255/50 ký tự, mô tả 500 ký tự.
  Giá từ 0 đến 9.999.999.999,99 đồng, tối đa hai chữ số thập phân.
- Chặn thuốc trùng tên và đơn vị đã chuẩn hóa, kể cả thuốc ngừng sử dụng.
  Cần ghi đủ hàm lượng/quy cách trong tên để phân biệt những thuốc khác nhau.
- Thuốc đã có lô kho hoặc chứng từ không được đổi sang đơn vị khác, tránh biến
  số lượng viên đã lưu thành hộp/chai. Tạo thuốc theo quy cách mới nếu cần.
- Thông báo thành công qua Sonner sau phản hồi; lỗi giữ tại biểu mẫu. Chặn bấm
  lưu lặp, trường bị khóa lúc lưu, hộp thoại hỗ trợ Tab/Escape và trả lại focus.

## API

Các endpoint yêu cầu JWT có vai trò Admin, đồng thời service kiểm tra tài khoản
đang hoạt động. Envelope là `HTTPResponseData<T>` như phần quản trị hiện có.

| Endpoint | Mục đích |
| --- | --- |
| `GET /api/admin/medicines?keyword=&status=all&page=1` | Tìm/lọc/phân trang; status gồm all, active, inactive |
| `GET /api/admin/medicines/{id}` | Chi tiết danh mục và tồn kho |
| `POST /api/admin/medicines` | Thêm name, unit, price, description |
| `PUT /api/admin/medicines/{id}` | Sửa các trường danh mục, kèm expectedVersion |
| `PUT /api/admin/medicines/{id}/status` | Đổi isActive, kèm expectedVersion |

`version` được tạo từ các trường danh mục, giá chuẩn hóa 2 chữ số thập phân.
Thay đổi tồn kho không làm phiên bản danh mục đổi. Sửa từ dữ liệu cũ trả 409
thay vì ghi đè. Mọi lần ghi dùng transaction và cùng khóa SQL workflow; nhật ký
Medicine/Create/Update/Activate/Deactivate lưu cùng transaction.

## Giá lịch sử và triển khai

Không sửa tồn kho, đơn thuốc hoặc dòng hóa đơn khi sửa danh mục. Sửa thêm phần
lập/dự tính hóa đơn để giá snapshot thuốc **bằng 0** không bị thay bằng giá danh
mục mới. Snapshot giá thuốc là nguồn tính tiền; hóa đơn đã lưu không tính lại.

Không cần migration mới cho FM-01. Database phải đã áp dụng đầy đủ các migrations
nghiệp vụ hiện có, đặc biệt
`20261001_add_prescription_snapshot_and_pharmacy.sql` (có backfill snapshot cũ).
Không dùng giá danh mục hôm nay để tự sửa giá đơn thuốc/hóa đơn lịch sử và không
chạy lại seed trên Azure để triển khai tính năng này.

Tên/đơn vị thuốc trên một số màn lịch sử vẫn tra từ danh mục hiện tại. FM-01 bảo
toàn snapshot **giá**, chưa bổ sung snapshot tên/đơn vị cho toàn bộ chứng từ.
Nhật ký Medicine được ghi ở backend; màn Nhật ký hệ thống hiện tập trung bệnh án,
chưa mở rộng giao diện đọc nhật ký danh mục thuốc.

## Kiểm thử

```powershell
dotnet run --project tests/MedicineCatalog
dotnet run --project tests/MedicineCatalog -- --sql
```

Mặc định dùng InMemory và chỉ dịch truy vấn SQL, không mở kết nối SQL. `--sql`
chỉ dùng SQL Server **localhost**, Windows authentication, database ngẫu nhiên
`FoMed_Medicine_Test_<GUID>`. Kiểm tra target trước tạo/xóa, không đọc appsettings
hoặc kết nối Azure; database tạm được xóa trong finally.

Tại repository frontend:

```powershell
node tests/medicine-catalog.audit.cjs
npm run lint
npm run build
```

Frontend kiểm tra render/handler với dữ liệu giả, không gọi HTTP. Các kiểm tra
này không thay thế UAT trình duyệt qua API thật hoặc nghiệm thu bản deploy.

## Kiểm tra thủ công sau khi chạy hoặc deploy

1. Admin mở Danh mục thuốc; tìm một thuốc ngoài trang đầu và lọc trạng thái.
2. Thêm thuốc DEMO tên có hàm lượng, đơn vị, giá; kiểm tra tồn không tự tăng.
3. Mở cùng thuốc ở hai tab; lưu tab đầu, tab còn lại phải bị chặn ghi đè.
4. Sửa giá; kiểm tra đơn/hóa đơn đã lưu giữ đúng giá snapshot, kể cả giá 0.
5. Ngừng thuốc còn đơn chưa cấp phải bị chặn. Thuốc đủ điều kiện ngừng vẫn giữ
   lô tồn và lịch sử, không xuất hiện trong danh mục kê đơn đang hoạt động.
6. Đăng nhập vai trò khác hoặc tài khoản khóa; API quản trị phải từ chối truy cập.

Chỉ dùng dữ liệu giả và database thử nghiệm riêng; không thao tác trên hồ sơ
bệnh nhân thật hay ghi dữ liệu demo lên Azure khi chưa được phép.

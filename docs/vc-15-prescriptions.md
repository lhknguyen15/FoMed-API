# VC-15 — Kê đơn thuốc

API thuộc module Clinical:

- `POST /api/clinical/records/{id}/prescription` tạo đơn cho bệnh án đang khám.
- `PUT /api/clinical/records/{id}/prescription` cập nhật đơn khi bệnh án chưa chốt.
- `GET /api/clinical/records/{id}/prescription` đọc đơn theo quyền bệnh nhân hoặc bác sĩ phụ trách.

Mỗi dòng thuốc lưu `unit_price_snapshot` để hóa đơn không thay đổi khi danh mục thuốc đổi giá. Nếu hồ sơ có dị ứng, request phải gửi `allergyAcknowledged: true`; API từ chối kê đơn nếu chưa xác nhận.

## Tìm thuốc và context bệnh nhân (mục 2)

- `GET /api/clinical/medicines/search?recordId={id}&keyword=&page=1`: Doctor-only và quyền phụ trách, phân trang 20 thuốc, trả tổng số kết quả và tồn khả dụng từ các lô chưa hết hạn.
- `GET /api/clinical/records/{id}/prescribing-context`: dị ứng lấy từ database, không phụ thuộc dữ liệu router; trả tồn các thuốc trong đơn và thuốc đang chọn qua query `medicineIds` tối đa 100 ID.
- POST/PUT đơn từ chối thuốc hết tồn/vượt tồn với HTTP 409; không trừ kho khi kê đơn, không giữ chỗ. Đơn đã cấp phát vẫn bị khóa theo mục 1.
- UI tìm kiếm có phân trang, không chọn thuốc hết tồn/thêm trùng; lỗi tồn giữ nội dung đang nhập. Lỗi context khóa lưu và cho tải lại.
- Chưa đối chiếu hoạt chất tự động: Allergies là văn bản, Medicine chưa có dữ liệu hoạt chất cấu trúc. Không coi “chưa ghi nhận dị ứng” là xác nhận thuốc an toàn.

Chi tiết quy tắc và cách kiểm thử trong `docs/prescription-search-stock-allergies.md`.

# VC-21 — Danh mục dịch vụ admin

Admin quản lý danh mục qua `GET/POST/PUT /api/admin/services`. Dịch vụ có mã không trùng, tên, mô tả, giá, chuyên khoa, thời lượng và trạng thái hoạt động. API không xóa bản ghi để giữ các lịch hẹn, chỉ định và hóa đơn đã tham chiếu; có thể ngừng dịch vụ bằng `IsActive=false`. Tạo/cập nhật đều ghi `audit.audit_logs`.

Migration cần chạy: `database/migrations/20261003_add_service_catalog_fields.sql`.

# VC-22 — Người dùng và phân quyền

Các route chỉ dành cho Admin:

- `GET /api/admin/users?search=&role=&isActive=&page=&pageSize=`: danh sách/lọc tài khoản.
- `GET /api/admin/roles`: danh sách role và số tài khoản.
- `PUT /api/admin/users/{id}/status`: khóa hoặc mở khóa tài khoản; khóa sẽ thu hồi refresh token.
- `PUT /api/admin/users/{id}/roles`: gán nhiều role và thu hồi các phiên cũ.
- `POST /api/admin/users/{id}/reset-password`: đặt mật khẩu mới và thu hồi phiên.

Mật khẩu không được trả về response. Các thao tác quản trị ghi audit log.

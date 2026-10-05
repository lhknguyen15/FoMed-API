# VC-08 — Hồ sơ bệnh nhân tại quầy

Các route thuộc nhóm Patient dành cho Receptionist/Admin:

- `GET /api/patients/staff?phone=...&name=...&patientCode=...&page=1&pageSize=20`: tìm hồ sơ bệnh nhân.
- `GET /api/patients/staff/{id}`: xem hồ sơ đầy đủ tại quầy.
- `POST /api/patients/staff`: tạo hồ sơ vãng lai chưa gắn tài khoản.
- `PUT /api/patients/staff/{id}`: cập nhật thông tin liên hệ, CCCD, BHYT, liên hệ khẩn cấp và dị ứng.
- `GET /api/patients/staff/{id}/history`: đọc lịch sử lịch hẹn của bệnh nhân.

Tác vụ tạo/cập nhật ghi vào `audit.audit_logs`. Các trường mở rộng được thêm bằng migration `20260930_add_frontdesk_patient_fields.sql`; cần chạy migration trước khi gọi các route mới.

# VC-13 — Màn hình khám

`POST /api/clinical/appointments/{appointmentId}/record` và `PUT /api/clinical/records/{id}` nhận thêm:

- `vitalSigns`: huyết áp tâm thu/tâm trương, mạch, nhiệt độ, cân nặng, chiều cao; API kiểm tra miền giá trị và lưu JSON snapshot.
- `icd10Code`: mã chẩn đoán ICD-10.
- `treatmentPlan`: hướng điều trị.
- `followUpDate`: ngày tái khám.

`MedicalRecordResponse` trả lại các trường trên cùng `isFinalized`/`finalizedAt`. Khi bác sĩ hoàn tất lịch, bệnh án được chốt trong cùng transaction; các API sửa bệnh án, kê đơn và chỉ định sau đó bị từ chối. Quyền tạo/sửa vẫn chỉ thuộc bác sĩ được phân công và lịch phải được check-in trước.

Migration cần chạy trên database cũ: `database/migrations/20260930_add_clinical_exam_and_order_snapshots.sql`.

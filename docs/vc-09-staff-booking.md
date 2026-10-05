# VC-09 — Đặt hộ và bệnh nhân vãng lai

Quy trình tại quầy gồm hai bước:

1. Tìm hồ sơ bằng `GET /api/patients/staff`; nếu chưa có thì tạo nhanh bằng `POST /api/patients/staff`.
2. Gọi `POST /api/appointments/staff-book` với `patientId`, `doctorId`, `startTime`, tùy chọn `serviceId` và `source`.

`source` nhận `1` cho đặt qua điện thoại và `2` cho khách đến trực tiếp. API lưu `patient_id`, `source`, `created_by`, `fee_snapshot` và lịch sử trạng thái, đồng thời áp dụng cùng kiểm tra lịch làm việc, ngày nghỉ và xung đột như đặt lịch trực tuyến. Route chỉ cho Receptionist/Admin.

# VC-02 — Đăng ký tài khoản bệnh nhân

FoMed-API hiện cung cấp hai endpoint theo Review ERD:

```http
GET /api/patients/lookup?phone=0901234567
POST /api/auth/register
```

Request đăng ký dùng `RegisterPatientRequest`:

```json
{
  "fullName": "Trần Thị B",
  "phone": "0901234567",
  "email": "b@example.com",
  "dateOfBirth": "1992-05-12T00:00:00",
  "password": "Password123!"
}
```

API luôn gán role `Patient`. Nếu có đúng một hồ sơ vãng lai chưa liên kết khớp số điện thoại, họ tên và ngày sinh, API gắn tài khoản mới vào hồ sơ cũ để giữ lịch sử khám. Nếu không có hồ sơ khớp, API tạo mã bệnh nhân mới. Việc liên kết không dựa vào số điện thoại đơn lẻ.

Response đăng ký dùng `AuthResponse`, giống login, gồm access token, refresh token, thời hạn và thông tin user. `GET /api/patients/lookup` trả `PatientDto` với thông tin tối thiểu để hỗ trợ xác minh; trường hợp nhiều hồ sơ cùng số điện thoại trả `409`.

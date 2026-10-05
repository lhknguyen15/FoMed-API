# Hợp đồng `forgot-password`

Theo màn VC-01 của Review ERD, FoMed-API cung cấp:

```http
POST /api/auth/forgot-password
Content-Type: application/json

{
  "email": "patient@example.com"
}
```

Request DTO là `ForgotPasswordRequest` với trường `Email` bắt buộc, đúng định dạng email và tối đa 256 ký tự. Response dùng `HTTPResponseData<string?>` của API hiện tại.

Endpoint chuẩn hóa email và luôn trả cùng một thông báo thành công để tránh dò xem email nào tồn tại. Việc phát hành reset token, lưu hash có thời hạn, gửi email và xác nhận mật khẩu mới cần được nối với email provider trước khi coi luồng khôi phục là hoàn tất end-to-end.

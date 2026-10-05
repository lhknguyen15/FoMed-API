# Quy ước hợp đồng API theo Review ERD

Tài liệu này là bảng chuẩn hóa tên cho FoMed-API theo các tên đã được nêu trong Review ERD. Đây là hợp đồng làm việc của API; các thay đổi route hoặc DTO phải cập nhật bảng này và bộ test cùng lúc.

## Quy ước nhóm module

Mỗi nhóm nghiệp vụ giữ cùng tên ở ba lớp:

| Nhóm | Controller | Service | DTO namespace |
|---|---|---|---|
| Auth | `AuthController` | `AuthService` | `FoMed.Application.DTO.Auth` |
| Patient | `PatientController`, `PatientLookupController` | `PatientService` | `FoMed.Application.DTO.Patient` |
| Doctor | `DoctorController`, `DoctorScheduleController` | `DoctorService`, `DoctorScheduleService` | `FoMed.Application.DTO.Doctor` |
| Doctor admin/Schedule | `DoctorAdminController`, `SpecialtyAdminController`, `DoctorTimeOffController`, `AdminTimeOffController` | `DoctorAdminService`, `DoctorTimeOffService` | `FoMed.Application.DTO.Doctor` |
| Appointment | `AppointmentController` | `AppointmentService` | `FoMed.Application.DTO.Appointment` |
| Clinical | `ClinicalController` | `ClinicalService` | `FoMed.Application.DTO.Clinical` |
| Billing | `InvoiceController` | `BillingService` | `FoMed.Application.DTO.Billing` |
| Profile | `ProfileController` | `ProfileService` | `FoMed.Application.DTO.Profile` |
| Pharmacy | `PharmacyController` | `PharmacyService` | `FoMed.Application.DTO.Pharmacy` |
| Catalog | `PublicCatalogController` | `DoctorService` | `FoMed.Application.DTO.Doctor`, `FoMed.Application.DTO.Clinical` |

Các controller được đặt trong thư mục theo nhóm:

```text
FoMed.Api/Controllers/
├── Auth/
├── Patient/
├── Doctor/
├── Appointment/
├── Clinical/
├── Billing/
├── Profile/
└── Catalog/
```

Namespace runtime vẫn là `FoMed.Api.Controllers` để không thay đổi hành vi discovery của ASP.NET; việc sắp xếp thư mục chỉ làm rõ module và không tạo route mới.

`BillingService` nằm trong `Services/Billing`, đúng nhóm nghiệp vụ. `InvoiceController` vẫn giữ tên endpoint hiện tại cho hóa đơn; khi Review cung cấp tên controller/API quản trị chính thức, đổi tên controller phải đi kèm route và OpenAPI, không đổi riêng file.

## DTO đã xác nhận từ Review

| Chức năng | Tên DTO chuẩn | Trạng thái |
|---|---|---|
| Đăng nhập | `LoginRequest` | Đã có |
| Phản hồi đăng nhập/đăng ký | `AuthResponse` | Đã có |
| Đăng ký bệnh nhân | `RegisterPatientRequest` | Đã có |
| Tra cứu bệnh nhân theo số điện thoại | `PatientDto` | Chuẩn hóa trong bước này |
| Quên mật khẩu | `ForgotPasswordRequest` | Đã có |
| Quản trị dịch vụ | `CreateServiceRequest`, `UpdateServiceRequest`, `ServiceCatalogResponse` | Đã có |
| Quản trị người dùng | `AdminUserResponse`, `UpdateUserRolesRequest`, `ResetUserPasswordRequest` | Đã có |
| Báo cáo | `ReportSummaryResponse`, `DoctorReportRow` | Đã có |
| Nhật ký | `AuditLogQuery`, `AuditLogResponse` | Đã có |
| Hồ sơ tài khoản | `ProfileResponse`, `UpdateProfileRequest`, `ChangePasswordRequest` | `ProfileResponse.Roles` hỗ trợ nhiều role |

Các DTO còn lại chỉ đổi tên sau khi đối chiếu đúng tên trong từng màn VC của Review. Không tự đặt thêm hậu tố `DTO` cho request/response; `*ResponseMessageDTO` chỉ chứa hằng số thông báo và không phải DTO dữ liệu.

## Route chuẩn đang sử dụng

```text
POST /api/auth/login
POST /api/auth/register
POST /api/auth/refresh
POST /api/auth/forgot-password

GET  /api/patients/lookup?phone=...
GET  /api/patients/staff?phone=...&name=...&patientCode=...
GET  /api/patients/staff/{id}
POST /api/patients/staff
PUT  /api/patients/staff/{id}
GET  /api/patients/staff/{id}/history
GET  /api/doctors
GET  /api/specialties
GET  /api/services

GET  /api/appointments/available-slots
POST /api/appointments/book
POST /api/appointments/staff-book
GET  /api/appointments/my-appointments
GET  /api/appointments/{id}
GET  /api/appointments/{id}/history
PUT  /api/appointments/{id}/reschedule
PUT  /api/appointments/{id}/cancel
GET  /api/appointments/staff-appointments?date=...&status=...&doctorId=...
PUT  /api/appointments/{id}/confirm
PUT  /api/appointments/{id}/check-in
PUT  /api/appointments/{id}/no-show
GET  /api/appointments/waiting-queue?date=...&doctorId=...
GET  /api/appointments/doctor-queue?date=...
POST /api/appointments/call-next?date=...&doctorId=...
PUT  /api/appointments/{id}/move-to-end

GET  /api/clinical/records
GET  /api/clinical/records/{id}/prescription
PUT  /api/clinical/records/{id}/prescription
GET  /api/clinical/records/{id}/services
PUT  /api/clinical/orders/{id}/cancel
GET  /api/clinical/lab-orders
POST /api/clinical/lab-orders/{id}/result
GET  /api/invoices
GET  /api/invoices/{id}
GET  /api/invoices/eligible?page=...
POST /api/invoices/{id}/payments
POST /api/invoices/{id}/cancel

GET  /api/pharmacy/inventory?page=...&medicineId=...&expiringBefore=...
POST /api/pharmacy/inventory/receipts
POST /api/pharmacy/inventory/adjustments
GET  /api/pharmacy/inventory/{batchId}/transactions?page=...
POST /api/pharmacy/prescriptions/{id}/dispense
POST /api/pharmacy/receipts

GET/POST/PUT /api/admin/doctors
GET/POST/PUT /api/admin/specialties
GET/POST/PUT/DELETE /api/doctor/time-off
GET/POST/PUT/DELETE /api/admin/time-off
GET/POST/PUT /api/admin/services
GET /api/admin/users?search=&role=&isActive=&page=&pageSize=
GET /api/admin/roles
PUT /api/admin/users/{id}/status
PUT /api/admin/users/{id}/roles
POST /api/admin/users/{id}/reset-password
GET /api/reports/summary?from=&to=&doctorId=
GET /api/reports/export?from=&to=&doctorId=
GET /api/audit-logs?entity=&action=&userId=&from=&to=&page=&pageSize=
```

Các route cho lễ tân, bác sĩ, lâm sàng và thanh toán được giữ trong module tương ứng. Không tạo song song hai URL cho cùng một nghiệp vụ; nếu Review yêu cầu route khác, cần lập migration contract và cập nhật FE/test trong cùng một thay đổi.

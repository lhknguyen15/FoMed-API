# Phân tích dotnet06 và định hướng FoMed

## 1. Kết luận nhanh

Project tham chiếu `dotnet06_cybersoftmarketplace` được tổ chức theo 4 phần chính:

- API: controllers, JWT, Swagger, CORS, SignalR.
- Application: services, DTO, helper và constants.
- Infrastructure: EF Core entities, DbContext, repositories và UnitOfWork.
- Web: Blazor frontend.

Flow nghiệp vụ chính:

```text
Controller -> Service -> UnitOfWork -> Repository -> DbContext
```

FoMed sẽ giữ flow này ở mức khái niệm, nhưng triển khai dependency direction đúng Clean Architecture:

```text
Api -> Application -> Domain
Api -> Infrastructure -> Application + Domain
```

`Application` chỉ phụ thuộc `Domain` và các abstraction của chính nó; `Infrastructure` implement các abstraction đó.

## 2. Những điểm nên học từ dotnet06

- Mỗi module đi trọn từ controller đến service/use case, repository và database.
- Controller chỉ nhận request, gọi application service và trả DTO.
- Không trả entity EF trực tiếp ra API.
- Có DTO riêng cho danh sách, chi tiết, tạo và cập nhật.
- Dùng UnitOfWork khi một use case cần nhiều repository cùng transaction.
- Có filter, pagination và response format nhất quán.
- Đăng ký dependency tập trung ở composition root của API.

## 3. Những điểm không sao chép

- Không để `Application` phụ thuộc `Infrastructure`.
- Không đặt persistence entity trong Infrastructure nếu entity đó chứa business rule; entity cốt lõi đặt ở Domain.
- Không để `IQueryable` xuyên qua application boundary.
- Không dùng `.Result` trong code async.
- Không lưu connection string, JWT key hoặc Redis password dạng plaintext trong source.
- Pipeline phải có đúng thứ tự `UseAuthentication()` trước `UseAuthorization()`.
- Không gọi `SaveChangesAsync()` trong từng thao tác repository nếu use case cần nhiều thay đổi; UnitOfWork quyết định thời điểm commit.

## 4. Định hướng Clean Architecture cho FoMed

### Domain

Chứa entity, enum, value object và business rule thuần:

- `User`, `Appointment`, `Patient`, `Doctor`.
- `MedicalRecord`, `LaboratoryOrder`, `LaboratoryResult`.
- `Medicine`, `Prescription`, `PrescriptionItem`.
- `ServiceCatalog`, `Invoice`, `InvoiceItem`.
- `AppointmentStatus`, `UserRole`, các trạng thái nghiệp vụ.

Domain không tham chiếu ASP.NET Core, EF Core hoặc JWT.

### Application

Chứa use case theo module:

- `Auth`: register, login, refresh token, profile.
- `Appointments`: book, reschedule, cancel, check-in, change status.
- `Examinations`: create medical record, diagnosis, assign service.
- `Laboratory`: create order, enter result, review result.
- `Prescriptions`: create prescription, dispense medicine.
- `Billing`: calculate invoice, record payment, payment history.
- `Reports`: appointment count and revenue summaries.

Mỗi module nên có request/response DTO, validator, service/use case và repository abstraction cần thiết.

### Infrastructure

Implement các abstraction của Application:

- `FoMedDbContext`.
- EF Core entity configurations.
- Repository và UnitOfWork.
- BCrypt password hasher.
- JWT token service.
- Email/SMS provider về sau.

Khuyến nghị dùng code-first và migration vì FoMed là project mới.

### Api

Chứa:

- Controllers hoặc endpoint groups.
- Authentication/authorization.
- Global exception handler.
- Validation error mapping.
- Swagger/OpenAPI.
- CORS theo environment.
- Health checks.
- Composition root và cấu hình DI.

## 5. Roadmap commit theo module

1. `chore: initialize backend clean architecture`
   - Đã hoàn tất: solution, 4 project, README và `.gitignore`.

2. `feat(infrastructure): add ef core persistence`
   - DbContext, mapping `User`/`Appointment`, SQL Server configuration.

3. `feat(auth): add jwt authentication`
   - BCrypt, token service, JWT validation, auth middleware và Swagger security.

4. `feat(auth): add registration and login endpoints`
   - User repository, register/login DTO, validation, unique email và `AuthController`.

5. `feat(appointments): add appointment management`
   - Đặt lịch, lịch của bệnh nhân, danh sách hôm nay, queue lễ tân và trạng thái lịch.

6. `feat(examinations): add medical examination workflow`
   - Hồ sơ khám, triệu chứng, chẩn đoán và chỉ định dịch vụ.

7. `feat(laboratory): add laboratory results`
   - Phiếu xét nghiệm, nhập kết quả và bác sĩ xem kết quả.

8. `feat(prescriptions): add prescription management`
   - Toa thuốc, tồn kho, cấp thuốc và hướng dẫn sử dụng.

9. `feat(billing): add invoice and payment management`
   - Tính phí khám, dịch vụ, thuốc và lịch sử thanh toán.

10. `test: add application and api coverage`
    - Unit test use case và integration test authorization/workflow.

11. `chore(api): add production middleware`
    - Exception handling, logging, health check, CORS và response format.

## 6. Trạng thái FoMed hiện tại

Đã có:

- `FoMed.Domain` với entity và enum nền tảng.
- `FoMed.Application` với auth abstractions và `AuthService`.
- `FoMed.Infrastructure` đã có package EF Core.
- `FoMed.Api` là composition root ban đầu.

Chưa có:

- DbContext và migration.
- Repository implementation và UnitOfWork.
- JWT/password implementation.
- Auth controller.
- Appointment workflow hoàn chỉnh.
- Test project.

Bước tiếp theo nên là hoàn thiện persistence làm một commit riêng, sau đó mới triển khai Auth. Không nên tiếp tục thêm nhiều module trước khi database boundary và quy ước use case được cố định.

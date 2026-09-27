# FoMed

Hệ thống quản lý phòng khám dịch vụ.

Repository này quản lý backend. Frontend dự kiến được quản lý riêng trong repository `FoMed-FE`.

## Tổng quan

Dự án hiện tập trung vào phần Backend của hệ thống, với cấu trúc được tổ chức theo hướng layer-based Clean Architecture tối giản để dễ mở rộng và làm việc theo từng module.

## Cấu trúc backend hiện tại

Project root hiện có thư mục `FoMed-API` chứa solution và 3 project chính:

- `FoMed.Api`: entry point của ứng dụng Web API, controller, middleware, cấu hình DI và authentication.
- `FoMed.Application`: DTO, business logic service layer, contract và các use case.
- `FoMed.Infrastructure`: EF Core, repository, unit of work, database models, JWT, BCrypt và các implementation phụ thuộc.

> Lưu ý: hiện tại không còn layer `FoMed.Domain` trong solution. README này đã được cập nhật để đúng với cấu trúc thực tế đang dùng.

## Kiến trúc và công nghệ

- ASP.NET Core Web API
- Entity Framework Core
- SQL Server
- JWT Authentication
- BCrypt password hashing
- Repository + UnitOfWork pattern
- Swagger (sẽ được cấu hình sau khi database và API đã sẵn sàng)

## Quy trình Git

Mỗi module nghiệp vụ sẽ được commit riêng để dễ theo dõi tiến độ, ví dụ:

```text
chore: initialize backend solution
feat(auth): add login and register
feat(users): add user management
feat(appointments): add appointment flow
feat(prescriptions): add prescription management
```

## Chạy backend

```powershell
cd FoMed-API
dotnet restore FoMed.sln
dotnet build FoMed.sln
dotnet run --project FoMed.Api
```

## Workflow kế tiếp

1. Tạo schema database SQL Server.
2. Scaffold EF Core model từ database.
3. Cấu hình Swagger để test API.
4. Hoàn thiện auth, appointment và các module nghiệp vụ tiếp theo.
5. Khi backend ổn định, triển khai frontend React hoặc UI tương ứng.

## Ghi chú

Project này đang được phát triển theo hướng backend-first, ưu tiên đúng cấu trúc và sạch code trước khi mở rộng thêm module và tính năng lớn hơn.

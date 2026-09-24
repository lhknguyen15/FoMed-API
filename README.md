# FoMed

Hệ thống quản lý phòng khám dịch vụ.

## Backend

Backend được tổ chức theo Clean Architecture:

- `FoMed.Domain`: entity và business rules thuần.
- `FoMed.Application`: use case, DTO và abstraction.
- `FoMed.Infrastructure`: database, authentication và external services.
- `FoMed.Api`: HTTP controllers, middleware và composition root.

## Quy trình Git

Mỗi module nghiệp vụ sẽ được triển khai bằng một commit riêng, ví dụ:

```text
chore: initialize backend clean architecture
feat(auth): add jwt authentication
feat(appointments): add appointment management
feat(prescriptions): add prescription management
```

## Chạy backend

```powershell
cd backend
dotnet restore FoMed.sln
dotnet build FoMed.sln
dotnet run --project FoMed.Api
```
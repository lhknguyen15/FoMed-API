using System.Text.Json;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Billing;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Billing;

public sealed class ServiceCatalogAdminService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<HTTPResponseData<IReadOnlyList<ServiceCatalogResponse>>> ListAsync(int userId, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct);
        var rows = await repository.Query<Service>().AsNoTracking().Include(s => s.Specialty).OrderBy(s => s.Name).ToListAsync(ct);
        return Ok<IReadOnlyList<ServiceCatalogResponse>>(rows.Select(Map).ToList());
    }
    public async Task<HTTPResponseData<ServiceCatalogResponse?>> CreateAsync(int userId, CreateServiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct); await RequireAdminAsync(userId, ct);
        var service = await BuildAsync(request.Code, request.Name, request.Description, request.Price, request.SpecialtyId, request.DurationMinutes, ct);
        repository.Add(service); await repository.SaveAsync(ct);
        repository.Add(new AuditLog { UserId = userId, Action = "Create", Entity = "Service", EntityId = service.Id, NewValue = JsonSerializer.Serialize(Map(service)), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return new HTTPResponseData<ServiceCatalogResponse?> { DataResponse = Map(service), Message = "Tạo dịch vụ thành công.", StatusCode = 201 };
    }
    public async Task<HTTPResponseData<ServiceCatalogResponse?>> UpdateAsync(int userId, int id, UpdateServiceRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct); await RequireAdminAsync(userId, ct);
        var service = await repository.Query<Service>().Include(s => s.Specialty).SingleOrDefaultAsync(s => s.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy dịch vụ.");
        var oldValue = JsonSerializer.Serialize(Map(service));
        await ValidateAsync(request.Code, request.Name, request.SpecialtyId, id, request.DurationMinutes, ct);
        service.Code = Normalize(request.Code); service.Name = request.Name.Trim(); service.Description = Normalize(request.Description); service.Price = request.Price;
        service.SpecialtyId = request.SpecialtyId; service.DurationMinutes = request.DurationMinutes; service.IsActive = request.IsActive ?? service.IsActive;
        service.Specialty = request.SpecialtyId.HasValue ? await repository.Query<Specialty>().SingleAsync(s => s.Id == request.SpecialtyId.Value, ct) : null;
        repository.Add(new AuditLog { UserId = userId, Action = "Update", Entity = "Service", EntityId = id, OldValue = oldValue, NewValue = JsonSerializer.Serialize(new { request.Code, request.Name, request.Price, request.SpecialtyId, request.DurationMinutes, request.IsActive }), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return new HTTPResponseData<ServiceCatalogResponse?> { DataResponse = Map(service), Message = "Cập nhật dịch vụ thành công.", StatusCode = 200 };
    }
    private async Task<Service> BuildAsync(string? code, string name, string? description, decimal price, int? specialtyId, int duration, CancellationToken ct)
    {
        await ValidateAsync(code, name, specialtyId, null, duration, ct);
        var service = new Service { Code = Normalize(code), Name = name.Trim(), Description = Normalize(description), Price = price, SpecialtyId = specialtyId, DurationMinutes = duration, IsActive = true };
        if (specialtyId.HasValue) service.Specialty = await repository.Query<Specialty>().SingleAsync(s => s.Id == specialtyId.Value, ct); return service;
    }
    private async Task ValidateAsync(string? code, string name, int? specialtyId, int? id, int duration, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ClinicException(400, "Tên dịch vụ là bắt buộc.");
        if (duration is < 5 or > 1440) throw new ClinicException(400, "Thời lượng dịch vụ không hợp lệ.");
        var normalizedCode = Normalize(code);
        if (normalizedCode != null && await repository.Query<Service>().AnyAsync(s => s.Code == normalizedCode && (!id.HasValue || s.Id != id.Value), ct)) throw new ClinicException(409, "Mã dịch vụ đã tồn tại.");
        if (specialtyId.HasValue && !await repository.Query<Specialty>().AnyAsync(s => s.Id == specialtyId.Value && s.IsActive, ct)) throw new ClinicException(400, "Chuyên khoa không hoạt động hoặc không tồn tại.");
    }
    private async Task RequireAdminAsync(int userId, CancellationToken ct) { if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý danh mục dịch vụ."); }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ServiceCatalogResponse Map(Service s) => new(s.Id, s.Code, s.Name, s.Description, s.Price, s.SpecialtyId, s.Specialty?.Name, s.DurationMinutes, s.IsActive);
    private static HTTPResponseData<T> Ok<T>(T value) => new() { DataResponse = value, Message = "Thành công.", StatusCode = 200 };
}

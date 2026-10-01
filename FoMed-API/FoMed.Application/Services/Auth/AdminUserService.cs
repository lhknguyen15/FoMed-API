using System.Text.Json;
using FoMed.Application.DTO;
using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Authentication;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Auth;

public sealed class AdminUserService(ClinicRepository repository, ClinicAccess access, IPasswordHasher passwordHasher)
{
    public async Task<(IReadOnlyList<AdminUserResponse> Items, int Total)> ListAsync(int userId, string? search, string? role, bool? isActive, int page, int pageSize, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct); if (page < 1 || pageSize is < 1 or > 100) throw new ClinicException(400, "Phân trang không hợp lệ.");
        var query = repository.Query<User>().AsNoTracking().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Include(u => u.Doctor).Include(u => u.Patient).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim(); query = query.Where(u => u.Username.Contains(term) || (u.FullName != null && u.FullName.Contains(term)) || (u.Email != null && u.Email.Contains(term)) || (u.Phone != null && u.Phone.Contains(term))); }
        if (!string.IsNullOrWhiteSpace(role)) query = query.Where(u => u.UserRoles.Any(r => r.Role.Name == role.Trim()));
        if (isActive.HasValue) query = query.Where(u => u.IsActive == isActive.Value);
        var total = await query.CountAsync(ct); var items = await query.OrderBy(u => u.Username).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return (items.Select(Map).ToList(), total);
    }
    public async Task<IReadOnlyList<AdminRoleResponse>> ListRolesAsync(int userId, CancellationToken ct)
    {
        await RequireAdminAsync(userId, ct); return await repository.Query<Role>().AsNoTracking().OrderBy(r => r.Name).Select(r => new AdminRoleResponse(r.Id, r.Name, r.UserRoles.Count)).ToListAsync(ct);
    }
    public async Task<HTTPResponseData<AdminUserResponse?>> UpdateStatusAsync(int actorId, int id, UpdateUserStatusRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct); await RequireAdminAsync(actorId, ct);
        var user = await LoadUserAsync(id, ct); if (actorId == id && !request.IsActive) throw new ClinicException(400, "Không thể tự khóa tài khoản đang đăng nhập.");
        var old = user.IsActive; user.IsActive = request.IsActive; user.UpdatedAt = DateTime.UtcNow;
        if (!request.IsActive) foreach (var token in user.RefreshTokens.Where(t => !t.RevokedAt.HasValue)) token.RevokedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = actorId, Action = request.IsActive ? "Activate" : "Deactivate", Entity = "User", EntityId = id, OldValue = JsonSerializer.Serialize(new { IsActive = old }), NewValue = JsonSerializer.Serialize(new { request.IsActive }), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct); return new HTTPResponseData<AdminUserResponse?> { DataResponse = Map(user), Message = "Cập nhật trạng thái người dùng thành công.", StatusCode = 200 };
    }
    public async Task<HTTPResponseData<AdminUserResponse?>> UpdateRolesAsync(int actorId, int id, UpdateUserRolesRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct); await RequireAdminAsync(actorId, ct);
        var user = await LoadUserAsync(id, ct); var roleNames = request.Roles.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roleNames.Length == 0) throw new ClinicException(400, "Phải chọn ít nhất một quyền.");
        var roles = await repository.Query<Role>().Where(r => roleNames.Contains(r.Name)).ToListAsync(ct); if (roles.Count != roleNames.Length) throw new ClinicException(400, "Có quyền không tồn tại.");
        var oldRoles = user.UserRoles.Select(x => x.Role.Name).ToArray();
        foreach (var link in user.UserRoles.Where(link => !roles.Any(role => role.Id == link.RoleId)).ToList()) { repository.Remove(link); user.UserRoles.Remove(link); }
        foreach (var role in roles.Where(role => !user.UserRoles.Any(link => link.RoleId == role.Id))) user.UserRoles.Add(new UserRole { UserId = id, RoleId = role.Id, Role = role });
        user.UpdatedAt = DateTime.UtcNow; foreach (var token in user.RefreshTokens.Where(t => !t.RevokedAt.HasValue)) token.RevokedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = actorId, Action = "AssignRoles", Entity = "User", EntityId = id, OldValue = JsonSerializer.Serialize(oldRoles), NewValue = JsonSerializer.Serialize(roleNames), CreatedAt = DateTime.UtcNow });
        await repository.SaveAsync(ct); await write.CommitAsync(ct); return new HTTPResponseData<AdminUserResponse?> { DataResponse = Map(user), Message = "Cập nhật quyền người dùng thành công; các phiên cũ đã bị thu hồi.", StatusCode = 200 };
    }
    public async Task<HTTPResponseData<string?>> ResetPasswordAsync(int actorId, int id, ResetUserPasswordRequest request, CancellationToken ct)
    {
        await using var write = await repository.BeginWriteAsync(ct); await RequireAdminAsync(actorId, ct); var user = await LoadUserAsync(id, ct);
        user.PasswordHash = passwordHasher.Hash(request.NewPassword); user.UpdatedAt = DateTime.UtcNow; foreach (var token in user.RefreshTokens.Where(t => !t.RevokedAt.HasValue)) token.RevokedAt = DateTime.UtcNow;
        repository.Add(new AuditLog { UserId = actorId, Action = "ResetPassword", Entity = "User", EntityId = id, CreatedAt = DateTime.UtcNow }); await repository.SaveAsync(ct); await write.CommitAsync(ct);
        return new HTTPResponseData<string?> { DataResponse = null, Message = "Đặt lại mật khẩu thành công; các phiên cũ đã bị thu hồi.", StatusCode = 200 };
    }
    private async Task<User> LoadUserAsync(int id, CancellationToken ct) => await repository.Query<User>().Include(u => u.UserRoles).ThenInclude(ur => ur.Role).Include(u => u.RefreshTokens).Include(u => u.Doctor).Include(u => u.Patient).SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new ClinicException(404, "Không tìm thấy người dùng.");
    private async Task RequireAdminAsync(int userId, CancellationToken ct) { if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được quản lý người dùng."); }
    private static AdminUserResponse Map(User u) => new(u.Id, u.Username, u.Email, u.FullName, u.Phone, u.IsActive, u.CreatedAt, u.UserRoles.Select(x => x.Role.Name).OrderBy(x => x).ToList(), u.Doctor?.Id, u.Patient?.Id);
}

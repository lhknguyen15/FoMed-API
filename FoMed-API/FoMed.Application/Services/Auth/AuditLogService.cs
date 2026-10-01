using FoMed.Application.DTO.Auth;
using FoMed.Application.Services.Clinical;
using FoMed.Infrastructure.Models;
using FoMed.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace FoMed.Application.Services.Auth;

public sealed class AuditLogService(ClinicRepository repository, ClinicAccess access)
{
    public async Task<(IReadOnlyList<AuditLogResponse> Items, int Total)> ListAsync(int userId, AuditLogQuery request, CancellationToken ct)
    {
        if (!await access.HasRoleAsync(userId, "Admin", ct)) throw new ClinicException(403, "Chỉ quản trị viên được xem nhật ký.");
        if (request.Page < 1 || request.PageSize is < 1 or > 200) throw new ClinicException(400, "Phân trang không hợp lệ.");
        var query = repository.Query<AuditLog>().AsNoTracking().Include(x => x.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Entity)) query = query.Where(x => x.Entity == request.Entity.Trim());
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(x => x.Action == request.Action.Trim());
        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (request.From.HasValue) query = query.Where(x => x.CreatedAt >= request.From.Value.ToUniversalTime());
        if (request.To.HasValue) query = query.Where(x => x.CreatedAt < request.To.Value.ToUniversalTime());
        var total = await query.CountAsync(ct); var items = await query.OrderByDescending(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return (items.Select(x => new AuditLogResponse(x.Id, x.UserId, x.User?.Username, x.Action, x.Entity, x.EntityId, x.OldValue, x.NewValue, x.CreatedAt)).ToList(), total);
    }
}

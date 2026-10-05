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
        if (request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 200) throw new ClinicException(400, "Phân trang không hợp lệ.");
        if (request.UserId is <= 0 || request.Entity?.Length > 100 || request.Action?.Length > 50)
            throw new ClinicException(400, "Bộ lọc nhật ký không hợp lệ.");
        var from = request.From.HasValue ? Utc(request.From.Value) : (DateTime?)null;
        var to = request.To.HasValue ? Utc(request.To.Value) : (DateTime?)null;
        if (from.HasValue && to.HasValue && from >= to) throw new ClinicException(400, "Khoảng thời gian không hợp lệ.");
        var query = repository.Query<AuditLog>().AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(request.Entity)) query = query.Where(x => x.Entity == request.Entity.Trim());
        if (!string.IsNullOrWhiteSpace(request.Action)) query = query.Where(x => x.Action == request.Action.Trim());
        if (request.UserId.HasValue) query = query.Where(x => x.UserId == request.UserId.Value);
        if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value);
        if (to.HasValue) query = query.Where(x => x.CreatedAt < to.Value);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.Id).Skip((request.Page - 1) * request.PageSize).Take(request.PageSize)
            .Include(x => x.User).ThenInclude(u => u!.UserRoles).ThenInclude(r => r.Role).ToListAsync(ct);
        return (items.Select(Map).ToList(), total);
    }

    private static DateTime Utc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Unspecified) return value.ToUniversalTime();
        if (value.Ticks < Appointment.ClinicTime.Offset.Ticks)
            throw new ClinicException(400, "Ngày lọc nhật ký không hợp lệ.");
        return DateTime.SpecifyKind(value.Subtract(Appointment.ClinicTime.Offset), DateTimeKind.Utc);
    }

    private static AuditLogResponse Map(AuditLog log)
    {
        var metadata = MedicalRecordAudit.ReadMetadata(log);
        var clinical = log.Entity == "MedicalRecord";
        return new(log.Id, log.UserId, metadata is null ? log.User?.Username : metadata.Actor.Username,
            log.Action, log.Entity, log.EntityId, clinical ? null : log.OldValue, clinical ? null : log.NewValue,
            DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc))
        {
            FullName = metadata is null ? log.User?.FullName : metadata.Actor.FullName,
            Roles = metadata?.Actor.Roles ?? log.User?.UserRoles.Select(r => r.Role.Name).Distinct().Order().ToArray() ?? [],
            ActorSnapshot = metadata is not null, Source = metadata?.Context.Source,
            IpAddress = metadata?.Context.IpAddress, RequestId = metadata?.Context.RequestId,
            ChangedFields = metadata?.ChangedFields ?? []
        };
    }
}

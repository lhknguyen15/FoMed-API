namespace FoMed.Application.DTO.Auth;

public sealed record AuditLogResponse(long AuditLogId, int? UserId, string? Username, string Action, string Entity, int? EntityId, string? OldValue, string? NewValue, DateTime CreatedAt);
public sealed record AuditLogQuery(string? Entity, string? Action, int? UserId, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50);

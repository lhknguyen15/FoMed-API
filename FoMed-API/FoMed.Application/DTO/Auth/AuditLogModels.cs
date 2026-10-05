namespace FoMed.Application.DTO.Auth;

public sealed record AuditLogResponse(long AuditLogId, int? UserId, string? Username, string Action, string Entity, int? EntityId, string? OldValue, string? NewValue, DateTime CreatedAt)
{
    public string? FullName { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public bool ActorSnapshot { get; init; }
    public string? Source { get; init; }
    public string? IpAddress { get; init; }
    public string? RequestId { get; init; }
    public IReadOnlyList<string> ChangedFields { get; init; } = [];
}
public sealed record AuditLogQuery(string? Entity, string? Action, int? UserId, DateTime? From, DateTime? To, int Page = 1, int PageSize = 50);

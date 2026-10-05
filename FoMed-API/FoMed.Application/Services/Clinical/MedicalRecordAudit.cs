using System.Text.Json;
using FoMed.Infrastructure.Models;

namespace FoMed.Application.Services.Clinical;

// HTTP owns transport metadata; service tests/other callers may have no HTTP context.
public interface IClinicalAuditContext
{
    string? IpAddress { get; }
    string? RequestId { get; }
}

public sealed record MedicalRecordAuditActor(int Id, string? Username, string? FullName, string[] Roles);
public sealed record MedicalRecordAuditContext(string Source, string? IpAddress, string? RequestId);
public sealed record MedicalRecordAuditMetadata(int Version, MedicalRecordAuditActor Actor,
    MedicalRecordAuditContext Context, string[] ChangedFields);

public static class MedicalRecordAudit
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AuditLog Create(int userId, User actor, string action, int? recordId, string source,
        IClinicalAuditContext? context = null, string[]? changedFields = null)
    {
        // Never copy symptoms, diagnosis, vital signs, notes, allergies or prescription contents.
        var metadata = new MedicalRecordAuditMetadata(1,
            new(userId, actor.Username, actor.FullName, actor.UserRoles.Select(r => r.Role.Name).Distinct().Order().ToArray()),
            new(source, context?.IpAddress, context?.RequestId), changedFields ?? []);
        return new AuditLog { UserId = userId, Action = action, Entity = "MedicalRecord", EntityId = recordId,
            NewValue = JsonSerializer.Serialize(metadata, JsonOptions), CreatedAt = DateTime.UtcNow };
    }

    public static MedicalRecordAuditMetadata? ReadMetadata(AuditLog log)
    {
        if (log.Entity != "MedicalRecord" || string.IsNullOrWhiteSpace(log.NewValue)) return null;
        try
        {
            var metadata = JsonSerializer.Deserialize<MedicalRecordAuditMetadata>(log.NewValue, JsonOptions);
            return metadata is { Version: 1, Actor: not null, Context: not null, ChangedFields: not null } &&
                metadata.Actor.Id == log.UserId && metadata.Actor.Roles is not null ? metadata : null;
        }
        catch (JsonException) { return null; } // Legacy string logs must not break the audit API.
    }
}

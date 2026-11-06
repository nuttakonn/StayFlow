using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Audit;

public class AuditEvent : Entity<Guid>
{
    public Guid? OrganizationId { get; private set; }
    public Guid? PropertyId { get; private set; }
    public Guid? ActorId { get; private set; }
    public string? ActorEmail { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; } = DateTimeOffset.UtcNow;
    public string EntityName { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty; // Created, Updated, Deleted
    public string? ChangesJson { get; private set; }

    private AuditEvent() { } // EF Core

    public static AuditEvent Create(
        Guid? organizationId,
        Guid? propertyId,
        Guid? actorId,
        string? actorEmail,
        string entityName,
        string entityId,
        string action,
        string? changesJson = null)
    {
        return new AuditEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ActorId = actorId,
            ActorEmail = actorEmail,
            TimestampUtc = DateTimeOffset.UtcNow,
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            ChangesJson = changesJson,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

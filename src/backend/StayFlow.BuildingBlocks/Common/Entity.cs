namespace StayFlow.BuildingBlocks.Common;

public abstract class Entity<TId>
{
    public TId Id { get; protected set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; protected set; }

    public void Touch()
    {
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

public abstract class TenantEntity<TId> : Entity<TId>
{
    public Guid OrganizationId { get; protected set; }
}

public abstract class PropertyEntity<TId> : TenantEntity<TId>
{
    public Guid PropertyId { get; protected set; }
}

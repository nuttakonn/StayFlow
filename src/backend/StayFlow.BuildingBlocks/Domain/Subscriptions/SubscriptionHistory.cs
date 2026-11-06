using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Subscriptions;

/// <summary>
/// Business-level narrative of subscription lifecycle events (distinct from the
/// generic field-diff audit that StayFlowDbContext.SaveChangesAsync already records
/// for every entity change). Kept small and append-only.
/// </summary>
public class SubscriptionHistory : TenantEntity<Guid>
{
    public Guid SubscriptionId { get; private set; }

    /// <summary>"TrialStarted" | "PlanChanged" | "Activated" | "MarkedPastDue" | "Cancelled"</summary>
    public string EventType { get; private set; } = string.Empty;
    public string? FromValue { get; private set; }
    public string? ToValue { get; private set; }
    public Guid? ActorUserId { get; private set; }

    private SubscriptionHistory() { } // EF Core

    public static SubscriptionHistory Record(
        Guid organizationId,
        Guid subscriptionId,
        string eventType,
        string? fromValue,
        string? toValue,
        Guid? actorUserId)
    {
        var history = new SubscriptionHistory
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            FromValue = fromValue,
            ToValue = toValue,
            ActorUserId = actorUserId
        };
        history.OrganizationId = organizationId;
        return history;
    }
}

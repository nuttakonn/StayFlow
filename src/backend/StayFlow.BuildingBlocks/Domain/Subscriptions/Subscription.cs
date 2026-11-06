using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Trialing = 1,
    Active = 2,
    PastDue = 3,
    Cancelled = 4
}

/// <summary>
/// An organization's SaaS subscription to a SubscriptionPlan. Only created for
/// DeploymentMode.SaaS organizations; On-Premise entitlements come from a signed
/// license instead (see docs/adr/0008), not from this entity.
/// </summary>
public class Subscription : TenantEntity<Guid>
{
    public Guid SubscriptionPlanId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? TrialEndsAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }

    private Subscription() { } // EF Core

    public static Subscription StartTrial(Guid organizationId, Guid subscriptionPlanId, int trialDays)
    {
        var now = DateTimeOffset.UtcNow;
        var subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            SubscriptionPlanId = subscriptionPlanId,
            Status = SubscriptionStatus.Trialing,
            StartedAtUtc = now,
            TrialEndsAtUtc = trialDays > 0 ? now.AddDays(trialDays) : (DateTimeOffset?)null
        };
        subscription.OrganizationId = organizationId;
        return subscription;
    }

    public Result ChangePlan(Guid newSubscriptionPlanId)
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict(
                "Subscription.Cancelled",
                "Cannot change the plan of a cancelled subscription."));
        }

        SubscriptionPlanId = newSubscriptionPlanId;
        Touch();
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict(
                "Subscription.Cancelled",
                "Cannot reactivate a cancelled subscription; create a new one."));
        }

        Status = SubscriptionStatus.Active;
        Touch();
        return Result.Success();
    }

    public Result MarkPastDue()
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict(
                "Subscription.Cancelled",
                "Cannot mark a cancelled subscription past due."));
        }

        Status = SubscriptionStatus.PastDue;
        Touch();
        return Result.Success();
    }

    public Result Cancel(string? reason)
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(Error.Conflict(
                "Subscription.AlreadyCancelled",
                "This subscription is already cancelled."));
        }

        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = DateTimeOffset.UtcNow;
        CancellationReason = reason;
        Touch();
        return Result.Success();
    }

    public bool IsTrialExpired(DateTimeOffset nowUtc) =>
        Status == SubscriptionStatus.Trialing && TrialEndsAtUtc.HasValue && nowUtc > TrialEndsAtUtc.Value;

    /// <summary>
    /// Whether this subscription currently grants its plan's entitlements.
    /// Provisional default: an expired trial grants nothing (hard stop) and PastDue
    /// still grants access (grace period) until explicitly cancelled. Both are
    /// simplest-safe-default choices pending a documented commercial policy (see
    /// docs/adr/0008) and can be revisited without changing this entity's shape.
    /// </summary>
    public bool IsEffectivelyActive(DateTimeOffset nowUtc) => Status switch
    {
        SubscriptionStatus.Active => true,
        SubscriptionStatus.PastDue => true,
        SubscriptionStatus.Trialing => !IsTrialExpired(nowUtc),
        SubscriptionStatus.Cancelled => false,
        _ => false
    };
}

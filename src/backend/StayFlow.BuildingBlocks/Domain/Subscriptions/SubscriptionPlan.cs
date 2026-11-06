using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Subscriptions;

/// <summary>
/// Platform-level catalog entry (NOT tenant-owned). Defines the capability keys and
/// usage limits a Subscription grants an organization. Plan pricing and billing-provider
/// wiring are explicitly out of scope for this increment (Phase 7 defers payment
/// provider integration); plans are assigned/recorded manually, matching how payments
/// are manually recorded elsewhere in this product (see Billing/Payment).
/// </summary>
public class SubscriptionPlan : Entity<Guid>
{
    /// <summary>Stable machine key (e.g. "starter"). Never renamed once referenced by a Subscription.</summary>
    public string Key { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    /// <summary>Null = unlimited.</summary>
    public int? MaxUsers { get; private set; }

    /// <summary>Null = unlimited.</summary>
    public int? MaxProperties { get; private set; }

    public int TrialDays { get; private set; } = 14;

    private readonly List<PlanEntitlement> _entitlements = [];
    public IReadOnlyCollection<PlanEntitlement> Entitlements => _entitlements.AsReadOnly();

    private SubscriptionPlan() { } // EF Core

    public static SubscriptionPlan Create(
        string key,
        string name,
        int? maxUsers,
        int? maxProperties,
        int trialDays,
        IEnumerable<string> capabilityKeys)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Plan key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Plan name is required.", nameof(name));

        var plan = new SubscriptionPlan
        {
            Id = Guid.NewGuid(),
            Key = key.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            MaxUsers = maxUsers,
            MaxProperties = maxProperties,
            TrialDays = trialDays,
            IsActive = true
        };

        foreach (var capabilityKey in capabilityKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            plan._entitlements.Add(PlanEntitlement.Create(plan.Id, capabilityKey));
        }

        return plan;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}

/// <summary>A single boolean capability key granted by a plan (the "PlanEntitlements" join table from the spec).</summary>
public class PlanEntitlement : Entity<Guid>
{
    public Guid SubscriptionPlanId { get; private set; }
    public string CapabilityKey { get; private set; } = string.Empty;

    private PlanEntitlement() { } // EF Core

    public static PlanEntitlement Create(Guid subscriptionPlanId, string capabilityKey)
    {
        return new PlanEntitlement
        {
            Id = Guid.NewGuid(),
            SubscriptionPlanId = subscriptionPlanId,
            CapabilityKey = capabilityKey.Trim()
        };
    }
}

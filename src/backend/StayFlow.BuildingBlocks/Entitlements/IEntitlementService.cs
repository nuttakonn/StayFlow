namespace StayFlow.BuildingBlocks.Entitlements;

/// <summary>
/// The effective, resolved entitlement set for one organization right now. Always
/// computed fresh from the Subscription + Plan (or the deployment license path) rather
/// than cached, so a plan change or cancellation takes effect on the next request.
/// </summary>
public record EffectiveEntitlements(
    string PlanKey,
    string PlanName,
    string Status,
    bool IsActive,
    DateTimeOffset? TrialEndsAtUtc,
    int? MaxUsers,
    int? MaxProperties,
    IReadOnlyCollection<string> Capabilities)
{
    public bool HasCapability(string capabilityKey) =>
        IsActive && Capabilities.Contains(capabilityKey, StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Resolves what an organization is entitled to right now. This is the single place
/// business modules call to check feature access server-side (CLAUDE.md 21.2);
/// no module should read SubscriptionPlan/Subscription rows directly.
/// </summary>
public interface IEntitlementService
{
    Task<EffectiveEntitlements> GetEffectiveEntitlementsAsync(Guid organizationId, CancellationToken ct = default);

    Task<bool> HasCapabilityAsync(Guid organizationId, string capabilityKey, CancellationToken ct = default);
}

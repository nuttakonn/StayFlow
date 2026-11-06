using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StayFlow.BuildingBlocks.Deployment;
using StayFlow.BuildingBlocks.Domain.Subscriptions;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Entitlements;

public class EntitlementService : IEntitlementService
{
    private readonly StayFlowDbContext _dbContext;
    private readonly DeploymentOptions _deploymentOptions;

    public EntitlementService(StayFlowDbContext dbContext, IOptions<DeploymentOptions> deploymentOptions)
    {
        _dbContext = dbContext;
        _deploymentOptions = deploymentOptions.Value;
    }

    public async Task<EffectiveEntitlements> GetEffectiveEntitlementsAsync(Guid organizationId, CancellationToken ct = default)
    {
        if (_deploymentOptions.Mode == DeploymentMode.OnPremise)
        {
            return ResolveOnPremiseEntitlements();
        }

        return await ResolveSaaSEntitlementsAsync(organizationId, ct);
    }

    public async Task<bool> HasCapabilityAsync(Guid organizationId, string capabilityKey, CancellationToken ct = default)
    {
        var entitlements = await GetEffectiveEntitlementsAsync(organizationId, ct);
        return entitlements.HasCapability(capabilityKey);
    }

    /// <summary>
    /// On-Premise entitlement resolution is provisional: signed-license verification
    /// (CLAUDE.md 21.3) is NOT implemented yet. Per "never implement fake license
    /// validation," this does not pretend to check a signature. Today every on-premise
    /// instance is granted the full capability set regardless of LicenseValidationEnabled,
    /// because turning that flag on without a real verifier would just be a different
    /// shape of fake validation. docs/adr/0008 records this as an explicit, provisional
    /// decision and the next task that must close it before any commercial on-premise
    /// release that relies on license-gated features.
    /// </summary>
    private EffectiveEntitlements ResolveOnPremiseEntitlements()
    {
        return new EffectiveEntitlements(
            PlanKey: "onpremise",
            PlanName: _deploymentOptions.InstanceName ?? "On-Premise License",
            Status: _deploymentOptions.LicenseValidationEnabled
                ? "LicenseValidationNotImplemented"
                : "Licensed",
            IsActive: true,
            TrialEndsAtUtc: null,
            MaxUsers: null,
            MaxProperties: null,
            Capabilities: EntitlementKeys.All);
    }

    private async Task<EffectiveEntitlements> ResolveSaaSEntitlementsAsync(Guid organizationId, CancellationToken ct)
    {
        var subscription = await _dbContext.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            return new EffectiveEntitlements(
                PlanKey: string.Empty,
                PlanName: "No Subscription",
                Status: "NoSubscription",
                IsActive: false,
                TrialEndsAtUtc: null,
                MaxUsers: null,
                MaxProperties: null,
                Capabilities: []);
        }

        var plan = await _dbContext.SubscriptionPlans
            .IgnoreQueryFilters()
            .Include(p => p.Entitlements)
            .FirstOrDefaultAsync(p => p.Id == subscription.SubscriptionPlanId, ct);

        if (plan is null)
        {
            // The plan was removed from the catalog after this subscription referenced it.
            // Fail closed rather than silently granting or guessing capabilities.
            return new EffectiveEntitlements(
                PlanKey: string.Empty,
                PlanName: "Unknown Plan",
                Status: "PlanNotFound",
                IsActive: false,
                TrialEndsAtUtc: null,
                MaxUsers: null,
                MaxProperties: null,
                Capabilities: []);
        }

        var now = DateTimeOffset.UtcNow;
        var isActive = subscription.IsEffectivelyActive(now);
        var status = subscription.IsTrialExpired(now) ? "TrialExpired" : subscription.Status.ToString();

        return new EffectiveEntitlements(
            PlanKey: plan.Key,
            PlanName: plan.Name,
            Status: status,
            IsActive: isActive,
            TrialEndsAtUtc: subscription.TrialEndsAtUtc,
            MaxUsers: plan.MaxUsers,
            MaxProperties: plan.MaxProperties,
            Capabilities: isActive
                ? plan.Entitlements.Select(e => e.CapabilityKey).ToList()
                : []);
    }
}

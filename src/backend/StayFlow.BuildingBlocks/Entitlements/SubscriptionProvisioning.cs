using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Deployment;
using StayFlow.BuildingBlocks.Domain.Subscriptions;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Entitlements;

/// <summary>
/// Shared "new organization" subscription provisioning, used by every code path that
/// creates an Organization (OrganizationEndpoints and both AuthEndpoints registration
/// flows) so they can't silently drift apart. On-Premise deployments intentionally
/// create no Subscription row; see docs/adr/0008.
/// </summary>
public static class SubscriptionProvisioning
{
    public const string DefaultTrialPlanKey = "starter";

    public static async Task StartTrialForNewOrganizationAsync(
        StayFlowDbContext dbContext,
        DeploymentOptions deployment,
        Guid organizationId,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (deployment.Mode != DeploymentMode.SaaS)
        {
            return;
        }

        var starterPlan = await dbContext.SubscriptionPlans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Key == DefaultTrialPlanKey && p.IsActive, ct);

        if (starterPlan is null)
        {
            // Catalog not seeded yet. The organization is created without a subscription;
            // IEntitlementService reports Status="NoSubscription" rather than silently
            // granting access, so this never shows up as a hidden privilege escalation.
            return;
        }

        var subscription = Subscription.StartTrial(organizationId, starterPlan.Id, starterPlan.TrialDays);
        dbContext.Subscriptions.Add(subscription);
        dbContext.SubscriptionHistories.Add(SubscriptionHistory.Record(
            organizationId, subscription.Id, "TrialStarted", null, starterPlan.Key, actorUserId));
    }
}

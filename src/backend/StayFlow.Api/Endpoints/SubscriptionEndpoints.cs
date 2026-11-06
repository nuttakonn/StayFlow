using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Authorization;
using StayFlow.BuildingBlocks.Domain.Subscriptions;
using StayFlow.BuildingBlocks.Entitlements;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record SubscriptionPlanDto(
    Guid Id,
    string Key,
    string Name,
    bool IsActive,
    int? MaxUsers,
    int? MaxProperties,
    int TrialDays,
    IReadOnlyCollection<string> Capabilities
);

public record EffectiveEntitlementsDto(
    string PlanKey,
    string PlanName,
    string Status,
    bool IsActive,
    DateTimeOffset? TrialEndsAtUtc,
    int? MaxUsers,
    int? MaxProperties,
    IReadOnlyCollection<string> Capabilities
);

public record ChangeSubscriptionPlanRequest(string PlanKey);
public record CancelSubscriptionRequest(string? Reason);

public static class SubscriptionEndpoints
{
    public static RouteGroupBuilder MapSubscriptionEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/subscription-plans", GetPlansAsync)
            .WithName("GetSubscriptionPlans")
            .WithSummary("List active subscription plans in the catalog")
            .Produces<List<SubscriptionPlanDto>>(StatusCodes.Status200OK);

        group.MapGet("/organizations/{organizationId:guid}/subscription", GetEffectiveEntitlementsAsync)
            .WithName("GetOrganizationSubscription")
            .WithSummary("Get the active organization's current subscription status and effective entitlements")
            .Produces<EffectiveEntitlementsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/organizations/{organizationId:guid}/subscription/change-plan", ChangePlanAsync)
            .WithName("ChangeSubscriptionPlan")
            .WithSummary("Change the organization's subscription plan (requires Subscriptions.Manage)")
            .Produces<EffectiveEntitlementsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/organizations/{organizationId:guid}/subscription/cancel", CancelAsync)
            .WithName("CancelSubscription")
            .WithSummary("Cancel the organization's subscription (requires Subscriptions.Manage)")
            .Produces<EffectiveEntitlementsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    private static async Task<IResult> GetPlansAsync(StayFlowDbContext dbContext, CancellationToken ct)
    {
        var plans = await dbContext.SubscriptionPlans
            .IgnoreQueryFilters()
            .Where(p => p.IsActive)
            .Include(p => p.Entitlements)
            .Select(p => new SubscriptionPlanDto(
                p.Id, p.Key, p.Name, p.IsActive, p.MaxUsers, p.MaxProperties, p.TrialDays,
                p.Entitlements.Select(e => e.CapabilityKey).ToList()))
            .ToListAsync(ct);

        return Results.Ok(plans);
    }

    private static async Task<IResult> GetEffectiveEntitlementsAsync(
        Guid organizationId,
        ITenantContext tenantContext,
        IEntitlementService entitlementService,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue || tenantContext.OrganizationId.Value != organizationId)
        {
            return Results.Problem("You do not have access to this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (!tenantContext.HasPermission(Permissions.SubscriptionsView))
        {
            return Results.Problem("You do not have permission to view this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        var entitlements = await entitlementService.GetEffectiveEntitlementsAsync(organizationId, ct);
        return Results.Ok(ToDto(entitlements));
    }

    private static async Task<IResult> ChangePlanAsync(
        Guid organizationId,
        [FromBody] ChangeSubscriptionPlanRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlementService,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue || tenantContext.OrganizationId.Value != organizationId)
        {
            return Results.Problem("You do not have access to this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (!tenantContext.HasPermission(Permissions.SubscriptionsManage))
        {
            return Results.Problem("You do not have permission to manage this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.PlanKey))
        {
            return Results.Problem("PlanKey is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var plan = await dbContext.SubscriptionPlans
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Key == request.PlanKey.Trim().ToLowerInvariant() && p.IsActive, ct);

        if (plan is null)
        {
            return Results.Problem($"Plan '{request.PlanKey}' was not found or is not active.", statusCode: StatusCodes.Status400BadRequest);
        }

        var subscription = await dbContext.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            return Results.Problem(
                "This organization has no subscription to change. On-Premise deployments manage entitlements through a license instead.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var fromPlanId = subscription.SubscriptionPlanId;
        var changeResult = subscription.ChangePlan(plan.Id);
        if (changeResult.IsFailure)
        {
            return Results.Problem(changeResult.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        dbContext.SubscriptionHistories.Add(SubscriptionHistory.Record(
            organizationId, subscription.Id, "PlanChanged",
            fromPlanId.ToString(), plan.Id.ToString(), tenantContext.UserId));

        await dbContext.SaveChangesAsync(ct);

        var entitlements = await entitlementService.GetEffectiveEntitlementsAsync(organizationId, ct);
        return Results.Ok(ToDto(entitlements));
    }

    private static async Task<IResult> CancelAsync(
        Guid organizationId,
        [FromBody] CancelSubscriptionRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlementService,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue || tenantContext.OrganizationId.Value != organizationId)
        {
            return Results.Problem("You do not have access to this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (!tenantContext.HasPermission(Permissions.SubscriptionsManage))
        {
            return Results.Problem("You do not have permission to manage this organization's subscription.", statusCode: StatusCodes.Status403Forbidden);
        }

        var subscription = await dbContext.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        if (subscription is null)
        {
            return Results.Problem("This organization has no subscription to cancel.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cancelResult = subscription.Cancel(request.Reason);
        if (cancelResult.IsFailure)
        {
            return Results.Problem(cancelResult.Error.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        dbContext.SubscriptionHistories.Add(SubscriptionHistory.Record(
            organizationId, subscription.Id, "Cancelled",
            null, request.Reason, tenantContext.UserId));

        await dbContext.SaveChangesAsync(ct);

        var entitlements = await entitlementService.GetEffectiveEntitlementsAsync(organizationId, ct);
        return Results.Ok(ToDto(entitlements));
    }

    private static EffectiveEntitlementsDto ToDto(EffectiveEntitlements e) => new(
        e.PlanKey, e.PlanName, e.Status, e.IsActive, e.TrialEndsAtUtc, e.MaxUsers, e.MaxProperties, e.Capabilities);
}

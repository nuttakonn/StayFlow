using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Rates;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record RateRuleRequest(
    string DayType,
    decimal? RateMultiplier,
    decimal? FlatAdjustment,
    string? SeasonStartDate,
    string? SeasonEndDate);

public record RatePlanRestrictionRequest(
    int? MinNights,
    int? MaxAdvanceDays,
    string[] BlackoutDates);

public record CreateRatePlanRequest(
    string Name,
    string Code,
    decimal BaseRate,
    string Currency,
    int RateType,
    bool IsPublic,
    Guid? RoomTypeId,
    RateRuleRequest[] Rules,
    RatePlanRestrictionRequest? Restrictions);

public record UpdateRatePlanRequest(
    string Name,
    decimal BaseRate,
    string Currency,
    bool IsPublic,
    RateRuleRequest[] Rules,
    RatePlanRestrictionRequest? Restrictions);

public record RateRuleDto(
    string DayType,
    decimal? RateMultiplier,
    decimal? FlatAdjustment,
    string? SeasonStartDate,
    string? SeasonEndDate);

public record RatePlanRestrictionDto(
    int? MinNights,
    int? MaxAdvanceDays,
    string[] BlackoutDates);

public record RatePlanDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string Code,
    decimal BaseRate,
    string Currency,
    string RateType,
    bool IsPublic,
    bool IsActive,
    Guid? RoomTypeId,
    RateRuleDto[] Rules,
    RatePlanRestrictionDto? Restrictions,
    DateTimeOffset CreatedAtUtc);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class RatePlanEndpoints
{
    public static void MapRatePlanEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/rate-plans")
            .WithTags("Rate Plans")
            .RequireAuthorization();

        auth.MapGet("/", ListRatePlansAsync);
        auth.MapPost("/", CreateRatePlanAsync);
        auth.MapPut("/{planId:guid}", UpdateRatePlanAsync);
        auth.MapDelete("/{planId:guid}", DeactivateRatePlanAsync);

        // Public endpoint — no auth required (used by booking engine)
        app.MapGet("/api/v1/properties/{propertyId:guid}/rate-plans/public", ListPublicRatePlansAsync)
            .WithTags("Rate Plans");
    }

    private static async Task<IResult> ListRatePlansAsync(
        Guid propertyId, StayFlowDbContext db, ITenantContext tenant)
    {
        var propertyExists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!propertyExists) return Results.NotFound();

        var plans = await db.RatePlans
            .Where(p => p.PropertyId == propertyId && p.IsActive)
            .Include(p => p.Rules)
            .Include(p => p.Restriction)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Results.Ok(new { data = plans.Select(ToDto), total = plans.Count });
    }

    private static async Task<IResult> ListPublicRatePlansAsync(
        Guid propertyId, StayFlowDbContext db)
    {
        var plans = await db.RatePlans
            .IgnoreQueryFilters()
            .Where(p => p.PropertyId == propertyId && p.IsActive && p.IsPublic)
            .Include(p => p.Rules)
            .Include(p => p.Restriction)
            .OrderBy(p => p.Name)
            .ToListAsync();

        return Results.Ok(new { data = plans.Select(ToDto), total = plans.Count });
    }

    private static async Task<IResult> CreateRatePlanAsync(
        Guid propertyId,
        [FromBody] CreateRatePlanRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();

        var rules = ParseRules(req.Rules);
        var restriction = ParseRestriction(req.Restrictions);

        var plan = RatePlan.Create(
            tenant.OrganizationId.Value,
            propertyId,
            req.Name,
            req.Code,
            req.BaseRate,
            req.Currency,
            (RateType)req.RateType,
            req.IsPublic,
            req.RoomTypeId,
            rules,
            restriction);

        db.RatePlans.Add(plan);
        foreach (var rule in plan.Rules) db.RateRules.Add(rule);
        if (plan.Restriction != null) db.RatePlanRestrictions.Add(plan.Restriction);

        await db.SaveChangesAsync();

        var saved = await db.RatePlans
            .Include(p => p.Rules)
            .Include(p => p.Restriction)
            .FirstAsync(p => p.Id == plan.Id);

        return Results.Created($"/api/v1/properties/{propertyId}/rate-plans/{plan.Id}", ToDto(saved));
    }

    private static async Task<IResult> UpdateRatePlanAsync(
        Guid propertyId, Guid planId,
        [FromBody] UpdateRatePlanRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var plan = await db.RatePlans
            .Include(p => p.Rules)
            .Include(p => p.Restriction)
            .FirstOrDefaultAsync(p => p.Id == planId && p.PropertyId == propertyId && p.IsActive);
        if (plan == null) return Results.NotFound();

        // Remove old rules/restriction before updating
        db.RateRules.RemoveRange(plan.Rules);
        if (plan.Restriction != null) db.RatePlanRestrictions.Remove(plan.Restriction);

        var rules = ParseRules(req.Rules);
        var restriction = ParseRestriction(req.Restrictions);
        plan.Update(req.Name, req.BaseRate, req.Currency, req.IsPublic, rules, restriction);

        foreach (var rule in plan.Rules) db.RateRules.Add(rule);
        if (plan.Restriction != null) db.RatePlanRestrictions.Add(plan.Restriction);

        await db.SaveChangesAsync();

        var saved = await db.RatePlans
            .Include(p => p.Rules)
            .Include(p => p.Restriction)
            .FirstAsync(p => p.Id == planId);

        return Results.Ok(ToDto(saved));
    }

    private static async Task<IResult> DeactivateRatePlanAsync(
        Guid propertyId, Guid planId, StayFlowDbContext db, ITenantContext tenant)
    {
        var plan = await db.RatePlans
            .FirstOrDefaultAsync(p => p.Id == planId && p.PropertyId == propertyId && p.IsActive);
        if (plan == null) return Results.NotFound();

        plan.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IEnumerable<(DayType, decimal?, decimal?, DateOnly?, DateOnly?)> ParseRules(
        RateRuleRequest[] rules) =>
        rules.Select(r =>
        {
            Enum.TryParse<DayType>(r.DayType, true, out var dt);
            DateOnly? start = r.SeasonStartDate != null ? DateOnly.Parse(r.SeasonStartDate) : null;
            DateOnly? end = r.SeasonEndDate != null ? DateOnly.Parse(r.SeasonEndDate) : null;
            return (dt, r.RateMultiplier, r.FlatAdjustment, start, end);
        });

    private static (int?, int?, IEnumerable<DateOnly>?)? ParseRestriction(
        RatePlanRestrictionRequest? r)
    {
        if (r == null) return null;
        var blackout = r.BlackoutDates?.Select(DateOnly.Parse) ?? [];
        return (r.MinNights, r.MaxAdvanceDays, blackout);
    }

    private static RatePlanDto ToDto(RatePlan p) => new(
        p.Id, p.PropertyId, p.Name, p.Code, p.BaseRate, p.Currency,
        p.RateType.ToString(), p.IsPublic, p.IsActive, p.RoomTypeId,
        p.Rules.Select(r => new RateRuleDto(
            r.DayType.ToString(), r.RateMultiplier, r.FlatAdjustment,
            r.SeasonStartDate?.ToString("yyyy-MM-dd"),
            r.SeasonEndDate?.ToString("yyyy-MM-dd"))).ToArray(),
        p.Restriction != null ? new RatePlanRestrictionDto(
            p.Restriction.MinNights,
            p.Restriction.MaxAdvanceDays,
            GetBlackoutDates(p.Restriction)) : null,
        p.CreatedAtUtc);

    private static string[] GetBlackoutDates(RatePlanRestriction r)
    {
        if (string.IsNullOrEmpty(r.BlackoutDatesJson)) return [];
        try { return System.Text.Json.JsonSerializer.Deserialize<string[]>(r.BlackoutDatesJson) ?? []; }
        catch { return []; }
    }
}

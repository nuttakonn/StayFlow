using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Services;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record PackageItemRequest(Guid ServiceItemId, int Quantity, string? Notes);

public record CreatePackageRequest(
    string Name,
    string? Description,
    string MealPlan,
    decimal RateAddOn,
    IReadOnlyList<PackageItemRequest>? Items);

public record UpdatePackageRequest(
    string Name,
    string? Description,
    string MealPlan,
    decimal RateAddOn,
    IReadOnlyList<PackageItemRequest>? Items);

public record PackageItemDto(Guid ServiceItemId, string ServiceItemName, int Quantity, string? Notes);

public record PackageDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string? Description,
    string MealPlan,
    decimal RateAddOn,
    bool IsActive,
    IReadOnlyList<PackageItemDto> Items,
    DateTimeOffset CreatedAtUtc);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class PackageEndpoints
{
    public static void MapPackageEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/packages")
            .WithTags("Packages")
            .RequireAuthorization();

        auth.MapGet("/", ListPackagesAsync);
        auth.MapPost("/", CreatePackageAsync);
        auth.MapGet("/{id:guid}", GetPackageAsync);
        auth.MapPut("/{id:guid}", UpdatePackageAsync);
        auth.MapDelete("/{id:guid}", DeactivatePackageAsync);
    }

    private static async Task<IResult> ListPackagesAsync(
        Guid propertyId,
        [FromQuery] bool? activeOnly,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        var query = db.Packages.Include(p => p.Items).Where(p => p.PropertyId == propertyId);
        if (activeOnly ?? true) query = query.Where(p => p.IsActive);

        var pkgs = await query.OrderBy(p => p.Name).ToListAsync();

        // Load service item names for display
        var serviceItemIds = pkgs.SelectMany(p => p.Items.Select(i => i.ServiceItemId)).Distinct().ToList();
        var serviceNames = await db.ServiceItems
            .Where(s => serviceItemIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);

        return Results.Ok(new { data = pkgs.Select(p => ToDto(p, serviceNames)), total = pkgs.Count });
    }

    private static async Task<IResult> CreatePackageAsync(
        Guid propertyId,
        [FromBody] CreatePackageRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        if (!Enum.TryParse<MealPlanType>(req.MealPlan, ignoreCase: true, out var mealPlan))
            return Results.Problem($"Invalid MealPlan '{req.MealPlan}'.", statusCode: 400);

        var items = req.Items?.Select(i => (i.ServiceItemId, i.Quantity, i.Notes));
        var pkg = Package.Create(tenant.OrganizationId.Value, propertyId,
            req.Name, req.Description, mealPlan, req.RateAddOn, items);

        db.Packages.Add(pkg);
        await db.SaveChangesAsync();

        var serviceNames = await LoadServiceNamesAsync(db, pkg);
        return Results.Created($"/api/v1/properties/{propertyId}/packages/{pkg.Id}", ToDto(pkg, serviceNames));
    }

    private static async Task<IResult> GetPackageAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var pkg = await db.Packages.Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id && p.PropertyId == propertyId);
        if (pkg == null) return Results.NotFound();

        var serviceNames = await LoadServiceNamesAsync(db, pkg);
        return Results.Ok(ToDto(pkg, serviceNames));
    }

    private static async Task<IResult> UpdatePackageAsync(
        Guid propertyId, Guid id,
        [FromBody] UpdatePackageRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var pkg = await db.Packages.Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id && p.PropertyId == propertyId);
        if (pkg == null) return Results.NotFound();

        if (!Enum.TryParse<MealPlanType>(req.MealPlan, ignoreCase: true, out var mealPlan))
            return Results.Problem($"Invalid MealPlan '{req.MealPlan}'.", statusCode: 400);

        var items = req.Items?.Select(i => (i.ServiceItemId, i.Quantity, i.Notes));
        pkg.Update(req.Name, req.Description, mealPlan, req.RateAddOn, items);

        // Replace package items — remove old ones, add new ones
        var oldItems = await db.PackageItems.Where(pi => pi.PackageId == id).ToListAsync();
        db.PackageItems.RemoveRange(oldItems);
        await db.SaveChangesAsync();

        var serviceNames = await LoadServiceNamesAsync(db, pkg);
        return Results.Ok(ToDto(pkg, serviceNames));
    }

    private static async Task<IResult> DeactivatePackageAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == id && p.PropertyId == propertyId);
        if (pkg == null) return Results.NotFound();
        pkg.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<Dictionary<Guid, string>> LoadServiceNamesAsync(StayFlowDbContext db, Package pkg)
    {
        var ids = pkg.Items.Select(i => i.ServiceItemId).Distinct().ToList();
        return await db.ServiceItems
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name);
    }

    private static PackageDto ToDto(Package p, Dictionary<Guid, string> serviceNames) =>
        new(p.Id, p.PropertyId, p.Name, p.Description, p.MealPlan.ToString(), p.RateAddOn, p.IsActive,
            p.Items.Select(i => new PackageItemDto(
                i.ServiceItemId,
                serviceNames.GetValueOrDefault(i.ServiceItemId, "Unknown"),
                i.Quantity, i.Notes)).ToList(),
            p.CreatedAtUtc);
}

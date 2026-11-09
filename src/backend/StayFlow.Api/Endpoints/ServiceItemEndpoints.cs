using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Services;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateServiceItemRequest(
    string Name,
    string? Description,
    decimal Price,
    string Unit,
    string Category);

public record UpdateServiceItemRequest(
    string Name,
    string? Description,
    decimal Price,
    string Unit,
    string Category);

public record ServiceItemDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string? Description,
    decimal Price,
    string Unit,
    string Category,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class ServiceItemEndpoints
{
    public static void MapServiceItemEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/service-items")
            .WithTags("Service Items")
            .RequireAuthorization();

        auth.MapGet("/", ListServiceItemsAsync);
        auth.MapPost("/", CreateServiceItemAsync);
        auth.MapGet("/{id:guid}", GetServiceItemAsync);
        auth.MapPut("/{id:guid}", UpdateServiceItemAsync);
        auth.MapDelete("/{id:guid}", DeactivateServiceItemAsync);
    }

    private static async Task<IResult> ListServiceItemsAsync(
        Guid propertyId,
        [FromQuery] string? category,
        [FromQuery] bool? activeOnly,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        var query = db.ServiceItems.Where(s => s.PropertyId == propertyId);

        if (Enum.TryParse<ServiceCategory>(category, ignoreCase: true, out var cat))
            query = query.Where(s => s.Category == cat);

        if (activeOnly ?? true)
            query = query.Where(s => s.IsActive);

        var items = await query.OrderBy(s => s.Category).ThenBy(s => s.Name).ToListAsync();
        return Results.Ok(new { data = items.Select(ToDto), total = items.Count });
    }

    private static async Task<IResult> CreateServiceItemAsync(
        Guid propertyId,
        [FromBody] CreateServiceItemRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        if (!Enum.TryParse<ServiceCategory>(req.Category, ignoreCase: true, out var cat))
            return Results.Problem($"Invalid category '{req.Category}'.", statusCode: 400);

        if (req.Price < 0)
            return Results.Problem("Price cannot be negative.", statusCode: 400);

        var item = ServiceItem.Create(tenant.OrganizationId.Value, propertyId,
            req.Name, req.Description, req.Price, req.Unit, cat);
        db.ServiceItems.Add(item);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/properties/{propertyId}/service-items/{item.Id}", ToDto(item));
    }

    private static async Task<IResult> GetServiceItemAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var item = await db.ServiceItems.FirstOrDefaultAsync(s => s.Id == id && s.PropertyId == propertyId);
        return item == null ? Results.NotFound() : Results.Ok(ToDto(item));
    }

    private static async Task<IResult> UpdateServiceItemAsync(
        Guid propertyId, Guid id,
        [FromBody] UpdateServiceItemRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var item = await db.ServiceItems.FirstOrDefaultAsync(s => s.Id == id && s.PropertyId == propertyId);
        if (item == null) return Results.NotFound();

        if (!Enum.TryParse<ServiceCategory>(req.Category, ignoreCase: true, out var cat))
            return Results.Problem($"Invalid category '{req.Category}'.", statusCode: 400);

        if (req.Price < 0)
            return Results.Problem("Price cannot be negative.", statusCode: 400);

        item.Update(req.Name, req.Description, req.Price, req.Unit, cat);
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(item));
    }

    private static async Task<IResult> DeactivateServiceItemAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var item = await db.ServiceItems.FirstOrDefaultAsync(s => s.Id == id && s.PropertyId == propertyId);
        if (item == null) return Results.NotFound();
        item.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static ServiceItemDto ToDto(ServiceItem s) =>
        new(s.Id, s.PropertyId, s.Name, s.Description, s.Price,
            s.Unit, s.Category.ToString(), s.IsActive, s.CreatedAtUtc);
}

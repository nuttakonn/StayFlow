using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Entitlements;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record CreatePropertyRequest(
    string Name,
    string Code,
    PropertyType BusinessType,
    string? IanaTimeZone,
    string? DefaultCurrency,
    string? Address,
    string? ContactPhone,
    string? ContactEmail
);

public record UpdatePropertyRequest(
    string Name,
    string? Address,
    string? ContactPhone,
    string? ContactEmail,
    string? IanaTimeZone,
    string? DefaultCurrency,
    string? CheckInTime,
    string? CheckOutTime
);

public record PropertyDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string Code,
    string BusinessType,
    string IanaTimeZone,
    string DefaultCurrency,
    string? Address,
    string? ContactPhone,
    string? ContactEmail,
    bool IsActive,
    string CheckInTime,
    string CheckOutTime,
    DateTimeOffset CreatedAtUtc
);

public static class PropertyEndpoints
{
    public static RouteGroupBuilder MapPropertyEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetPropertiesAsync)
            .WithName("GetProperties")
            .WithSummary("List all properties in the active organization")
            .Produces<List<PropertyDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/", CreatePropertyAsync)
            .WithName("CreateProperty")
            .WithSummary("Create a new property (Hotel, Apartment, or Hybrid)")
            .Produces<PropertyDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetPropertyByIdAsync)
            .WithName("GetPropertyById")
            .WithSummary("Get details of a property by ID")
            .Produces<PropertyDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdatePropertyAsync)
            .WithName("UpdateProperty")
            .WithSummary("Update property details and check-in/out policy (Requires PropertiesManage)")
            .Produces<PropertyDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetPropertiesAsync(
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
        {
            return Results.Problem("An active organization context is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var properties = await dbContext.Properties
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .Select(p => new PropertyDto(
                p.Id,
                p.OrganizationId,
                p.Name,
                p.Code,
                p.BusinessType.ToString(),
                p.IanaTimeZone,
                p.DefaultCurrency,
                p.Address,
                p.ContactPhone,
                p.ContactEmail,
                p.IsActive,
                p.CheckInTime.ToString("HH:mm"),
                p.CheckOutTime.ToString("HH:mm"),
                p.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(properties);
    }

    private static async Task<IResult> CreatePropertyAsync(
        [FromBody] CreatePropertyRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IEntitlementService entitlementService,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
        {
            return Results.Problem("An active organization context is required to create a property.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Server-side entitlement check (CLAUDE.md 21.2): frontend visibility of a
        // "create property" button is never a substitute for this.
        var entitlements = await entitlementService.GetEffectiveEntitlementsAsync(tenantContext.OrganizationId.Value, ct);
        if (!entitlements.IsActive)
        {
            return Results.Problem(
                $"The organization's subscription is not active (status: {entitlements.Status}). Properties cannot be created.",
                statusCode: StatusCodes.Status402PaymentRequired);
        }

        if (entitlements.MaxProperties.HasValue)
        {
            var activePropertyCount = await dbContext.Properties
                .CountAsync(p => p.OrganizationId == tenantContext.OrganizationId.Value && p.IsActive, ct);

            if (activePropertyCount >= entitlements.MaxProperties.Value)
            {
                return Results.Problem(
                    $"The '{entitlements.PlanName}' plan allows at most {entitlements.MaxProperties.Value} active properties. Upgrade the plan to add more.",
                    statusCode: StatusCodes.Status403Forbidden);
            }
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.Problem("Property Name and Code are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cleanCode = request.Code.Trim().ToUpperInvariant();
        var exists = await dbContext.Properties
            .AnyAsync(p => p.OrganizationId == tenantContext.OrganizationId.Value && p.Code == cleanCode, ct);

        if (exists)
        {
            return Results.Problem($"Property code '{cleanCode}' already exists in this organization.", statusCode: StatusCodes.Status400BadRequest);
        }

        var property = Property.Create(
            organizationId: tenantContext.OrganizationId.Value,
            name: request.Name,
            code: cleanCode,
            businessType: request.BusinessType,
            ianaTimeZone: request.IanaTimeZone ?? "Asia/Bangkok",
            defaultCurrency: request.DefaultCurrency ?? "THB",
            address: request.Address,
            contactPhone: request.ContactPhone,
            contactEmail: request.ContactEmail
        );

        dbContext.Properties.Add(property);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/properties/{property.Id}", new PropertyDto(
            property.Id,
            property.OrganizationId,
            property.Name,
            property.Code,
            property.BusinessType.ToString(),
            property.IanaTimeZone,
            property.DefaultCurrency,
            property.Address,
            property.ContactPhone,
            property.ContactEmail,
            property.IsActive,
            property.CheckInTime.ToString("HH:mm"),
            property.CheckOutTime.ToString("HH:mm"),
            property.CreatedAtUtc
        ));
    }

    private static async Task<IResult> UpdatePropertyAsync(
        Guid id,
        [FromBody] UpdatePropertyRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Problem("An active organization context is required.", statusCode: StatusCodes.Status400BadRequest);

        var property = await dbContext.Properties.FirstOrDefaultAsync(
            p => p.Id == id && p.OrganizationId == tenantContext.OrganizationId.Value, ct);

        if (property == null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Name))
            return Results.Problem("Property name is required.", statusCode: StatusCodes.Status400BadRequest);

        property.UpdateDetails(
            request.Name,
            request.Address,
            request.ContactPhone,
            request.ContactEmail,
            request.IanaTimeZone ?? property.IanaTimeZone,
            request.DefaultCurrency ?? property.DefaultCurrency);

        if (!string.IsNullOrWhiteSpace(request.CheckInTime) && !string.IsNullOrWhiteSpace(request.CheckOutTime)
            && TimeOnly.TryParse(request.CheckInTime, out var checkIn)
            && TimeOnly.TryParse(request.CheckOutTime, out var checkOut))
        {
            property.ConfigureCheckInOutPolicy(checkIn, checkOut);
        }

        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new PropertyDto(
            property.Id, property.OrganizationId, property.Name, property.Code,
            property.BusinessType.ToString(), property.IanaTimeZone, property.DefaultCurrency,
            property.Address, property.ContactPhone, property.ContactEmail,
            property.IsActive, property.CheckInTime.ToString("HH:mm"), property.CheckOutTime.ToString("HH:mm"),
            property.CreatedAtUtc));
    }

    private static async Task<IResult> GetPropertyByIdAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
        {
            return Results.Problem("An active organization context is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var property = await dbContext.Properties.FindAsync([id], ct);
        if (property == null) return Results.NotFound();

        return Results.Ok(new PropertyDto(
            property.Id,
            property.OrganizationId,
            property.Name,
            property.Code,
            property.BusinessType.ToString(),
            property.IanaTimeZone,
            property.DefaultCurrency,
            property.Address,
            property.ContactPhone,
            property.ContactEmail,
            property.IsActive,
            property.CheckInTime.ToString("HH:mm"),
            property.CheckOutTime.ToString("HH:mm"),
            property.CreatedAtUtc
        ));
    }
}

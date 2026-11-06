using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Rates;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateCouponRequest(
    string Code,
    int DiscountType,       // 0=Percentage, 1=FixedAmount, 2=FreeNight
    decimal Value,
    int? MaxUses,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    Guid[]? EligibleRoomTypeIds);

public record CouponDto(
    Guid Id,
    Guid PropertyId,
    string Code,
    string DiscountType,
    decimal Value,
    int? MaxUses,
    int UsedCount,
    DateOnly? ValidFrom,
    DateOnly? ValidTo,
    bool IsActive,
    DateTimeOffset CreatedAtUtc);

public record ValidateCouponRequest(
    string Code,
    Guid RoomTypeId,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    decimal BaseAmount);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class CouponEndpoints
{
    public static void MapCouponEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/coupons")
            .WithTags("Coupons")
            .RequireAuthorization();

        auth.MapGet("/", ListCouponsAsync);
        auth.MapPost("/", CreateCouponAsync);
        auth.MapDelete("/{couponId:guid}", DeactivateCouponAsync);
        auth.MapPost("/validate", ValidateCouponAsync);
    }

    private static async Task<IResult> ListCouponsAsync(
        Guid propertyId, StayFlowDbContext db, ITenantContext tenant)
    {
        var propertyExists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!propertyExists) return Results.NotFound();

        var coupons = await db.Coupons
            .Where(c => c.PropertyId == propertyId && c.IsActive)
            .OrderBy(c => c.Code)
            .ToListAsync();

        return Results.Ok(new { data = coupons.Select(ToDto), total = coupons.Count });
    }

    private static async Task<IResult> CreateCouponAsync(
        Guid propertyId,
        [FromBody] CreateCouponRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();

        // Case-insensitive unique code check
        var codeUpper = req.Code.Trim().ToUpperInvariant();
        var exists = await db.Coupons.AnyAsync(c =>
            c.PropertyId == propertyId && c.Code == codeUpper && c.IsActive);
        if (exists)
            return Results.Problem($"A coupon with code '{codeUpper}' already exists.", statusCode: 409);

        var coupon = Coupon.Create(
            tenant.OrganizationId.Value,
            propertyId,
            req.Code,
            (DiscountType)req.DiscountType,
            req.Value,
            req.MaxUses,
            req.ValidFrom,
            req.ValidTo,
            req.EligibleRoomTypeIds);

        db.Coupons.Add(coupon);
        await db.SaveChangesAsync();

        return Results.Created($"/api/v1/properties/{propertyId}/coupons/{coupon.Id}", ToDto(coupon));
    }

    private static async Task<IResult> DeactivateCouponAsync(
        Guid propertyId, Guid couponId, StayFlowDbContext db, ITenantContext tenant)
    {
        var coupon = await db.Coupons
            .FirstOrDefaultAsync(c => c.Id == couponId && c.PropertyId == propertyId && c.IsActive);
        if (coupon == null) return Results.NotFound();

        coupon.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> ValidateCouponAsync(
        Guid propertyId,
        [FromBody] ValidateCouponRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var codeUpper = req.Code.Trim().ToUpperInvariant();
        var coupon = await db.Coupons
            .FirstOrDefaultAsync(c => c.PropertyId == propertyId && c.Code == codeUpper);

        if (coupon == null)
            return Results.Ok(new { isValid = false, reason = "Coupon code not found.", discountAmount = 0m });

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reason = coupon.InvalidReason(today);
        if (reason != null)
            return Results.Ok(new { isValid = false, reason, discountAmount = 0m });

        var discount = coupon.CalculateDiscount(req.BaseAmount);
        return Results.Ok(new { isValid = true, reason = (string?)null, discountAmount = discount });
    }

    private static CouponDto ToDto(Coupon c) => new(
        c.Id, c.PropertyId, c.Code, c.DiscountType.ToString(), c.Value,
        c.MaxUses, c.UsedCount, c.ValidFrom, c.ValidTo, c.IsActive, c.CreatedAtUtc);
}

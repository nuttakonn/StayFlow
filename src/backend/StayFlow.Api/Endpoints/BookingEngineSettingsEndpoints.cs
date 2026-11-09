using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.BookingEngine;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record BookingEngineSettingsDto(
    Guid PropertyId,
    bool IsEnabled,
    int HoldDurationMinutes,
    string BookingPolicyText,
    int MinAdvanceBookingDays,
    int MaxAdvanceBookingDays);

public record UpdateBookingEngineSettingsRequest(
    bool IsEnabled,
    int HoldDurationMinutes,
    string BookingPolicyText,
    int MinAdvanceBookingDays,
    int MaxAdvanceBookingDays);

public static class BookingEngineSettingsEndpoints
{
    public static void MapBookingEngineSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/properties/{propId:guid}/booking-engine")
            .WithTags("Booking Engine")
            .RequireAuthorization();

        // GET settings — returns defaults if no record exists yet
        group.MapGet("/settings", async (
            Guid propId,
            StayFlowDbContext db,
            ITenantContext tenant) =>
        {
            if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

            var property = await db.Properties.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == propId);
            if (property == null || property.OrganizationId != tenant.OrganizationId.Value)
                return Results.NotFound();

            var settings = await db.BookingEngineSettings.AsNoTracking()
                .FirstOrDefaultAsync(s => s.PropertyId == propId);

            if (settings == null)
            {
                var defaults = BookingEngineSettings.CreateDefault(property.OrganizationId, propId);
                return Results.Ok(ToDto(defaults));
            }

            return Results.Ok(ToDto(settings));
        }).WithName("GetBookingEngineSettings");

        // PUT settings — create or update
        group.MapPut("/settings", async (
            Guid propId,
            UpdateBookingEngineSettingsRequest req,
            StayFlowDbContext db,
            ITenantContext tenant) =>
        {
            if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

            if (req.HoldDurationMinutes is < 5 or > 60)
                return Results.BadRequest("HoldDurationMinutes must be between 5 and 60.");
            if (req.MinAdvanceBookingDays < 0 || req.MaxAdvanceBookingDays < req.MinAdvanceBookingDays)
                return Results.BadRequest("Invalid advance booking day range.");

            var property = await db.Properties
                .FirstOrDefaultAsync(p => p.Id == propId);
            if (property == null || property.OrganizationId != tenant.OrganizationId.Value)
                return Results.NotFound();

            var settings = await db.BookingEngineSettings
                .FirstOrDefaultAsync(s => s.PropertyId == propId);

            if (settings == null)
            {
                settings = BookingEngineSettings.CreateDefault(property.OrganizationId, propId);
                db.BookingEngineSettings.Add(settings);
            }

            settings.Update(req.IsEnabled, req.HoldDurationMinutes,
                req.BookingPolicyText, req.MinAdvanceBookingDays, req.MaxAdvanceBookingDays);

            await db.SaveChangesAsync();
            return Results.Ok(ToDto(settings));
        }).WithName("UpdateBookingEngineSettings");
    }

    private static BookingEngineSettingsDto ToDto(BookingEngineSettings s) => new(
        s.PropertyId, s.IsEnabled, s.HoldDurationMinutes,
        s.BookingPolicyText, s.MinAdvanceBookingDays, s.MaxAdvanceBookingDays);
}

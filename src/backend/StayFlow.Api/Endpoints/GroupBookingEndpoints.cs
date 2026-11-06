using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Rates;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateGroupBookingRequest(
    string CoordinatorName,
    string? CoordinatorEmail,
    string? CoordinatorPhone,
    string? CompanyName,
    string? AgreedRateNotes,
    string? Notes);

public record AddReservationToGroupRequest(Guid ReservationId);

public record CancelGroupBookingRequest(string Reason);

public record GroupBookingDto(
    Guid Id,
    Guid PropertyId,
    string CoordinatorName,
    string? CoordinatorEmail,
    string? CompanyName,
    string? AgreedRateNotes,
    string Status,
    int ReservationCount,
    DateTimeOffset CreatedAtUtc);

public record GroupReservationSummaryDto(
    Guid Id,
    string? GuestName,
    string CheckInDate,
    string CheckOutDate,
    string Status,
    decimal TotalAmount);

public record GroupBookingDetailDto(
    Guid Id,
    Guid PropertyId,
    string CoordinatorName,
    string? CoordinatorEmail,
    string? CoordinatorPhone,
    string? CompanyName,
    string? AgreedRateNotes,
    string? Notes,
    string Status,
    string? CancellationReason,
    DateTimeOffset? CancelledAtUtc,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<GroupReservationSummaryDto> Reservations);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class GroupBookingEndpoints
{
    public static void MapGroupBookingEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/group-bookings")
            .WithTags("Group Bookings")
            .RequireAuthorization();

        auth.MapGet("/", ListGroupBookingsAsync);
        auth.MapPost("/", CreateGroupBookingAsync);
        auth.MapGet("/{groupId:guid}", GetGroupBookingAsync);
        auth.MapPost("/{groupId:guid}/reservations", AddReservationAsync);
        auth.MapPost("/{groupId:guid}/cancel", CancelGroupBookingAsync);
    }

    private static async Task<IResult> ListGroupBookingsAsync(
        Guid propertyId, StayFlowDbContext db, ITenantContext tenant)
    {
        var propertyExists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!propertyExists) return Results.NotFound();

        var groups = await db.GroupBookings
            .Where(g => g.PropertyId == propertyId)
            .OrderByDescending(g => g.CreatedAtUtc)
            .ToListAsync();

        // Count linked reservations per group
        var groupIds = groups.Select(g => g.Id).ToList();
        var counts = await db.Reservations
            .Where(r => r.GroupBookingId.HasValue && groupIds.Contains(r.GroupBookingId!.Value))
            .GroupBy(r => r.GroupBookingId!.Value)
            .Select(g => new { GroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GroupId, x => x.Count);

        var dtos = groups.Select(g => new GroupBookingDto(
            g.Id, g.PropertyId, g.CoordinatorName, g.CoordinatorEmail,
            g.CompanyName, g.AgreedRateNotes, g.Status.ToString(),
            counts.GetValueOrDefault(g.Id, 0), g.CreatedAtUtc)).ToList();

        return Results.Ok(new { data = dtos, total = dtos.Count });
    }

    private static async Task<IResult> CreateGroupBookingAsync(
        Guid propertyId,
        [FromBody] CreateGroupBookingRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null) return Results.NotFound();

        var group = GroupBooking.Create(
            tenant.OrganizationId.Value,
            propertyId,
            req.CoordinatorName,
            req.CoordinatorEmail,
            req.CoordinatorPhone,
            req.CompanyName,
            req.AgreedRateNotes,
            req.Notes);

        db.GroupBookings.Add(group);
        await db.SaveChangesAsync();

        return Results.Created($"/api/v1/properties/{propertyId}/group-bookings/{group.Id}",
            new GroupBookingDto(group.Id, group.PropertyId, group.CoordinatorName,
                group.CoordinatorEmail, group.CompanyName, group.AgreedRateNotes,
                group.Status.ToString(), 0, group.CreatedAtUtc));
    }

    private static async Task<IResult> GetGroupBookingAsync(
        Guid propertyId, Guid groupId, StayFlowDbContext db, ITenantContext tenant)
    {
        var group = await db.GroupBookings
            .FirstOrDefaultAsync(g => g.Id == groupId && g.PropertyId == propertyId);
        if (group == null) return Results.NotFound();

        var reservations = await db.Reservations
            .Where(r => r.GroupBookingId == groupId)
            .Join(db.Guests, r => r.GuestId, g => g.Id,
                (r, g) => new GroupReservationSummaryDto(
                    r.Id, g.FullName,
                    r.CheckInDate.ToString("yyyy-MM-dd"),
                    r.CheckOutDate.ToString("yyyy-MM-dd"),
                    r.Status.ToString(), r.TotalAmount))
            .ToListAsync();

        return Results.Ok(new GroupBookingDetailDto(
            group.Id, group.PropertyId, group.CoordinatorName, group.CoordinatorEmail,
            group.CoordinatorPhone, group.CompanyName, group.AgreedRateNotes, group.Notes,
            group.Status.ToString(), group.CancellationReason, group.CancelledAtUtc,
            group.CreatedAtUtc, reservations));
    }

    private static async Task<IResult> AddReservationAsync(
        Guid propertyId, Guid groupId,
        [FromBody] AddReservationToGroupRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var group = await db.GroupBookings
            .FirstOrDefaultAsync(g => g.Id == groupId && g.PropertyId == propertyId);
        if (group == null) return Results.NotFound();
        if (group.Status == GroupBookingStatus.Cancelled)
            return Results.Problem("Cannot add reservations to a cancelled group.", statusCode: 409);

        // Reservation must belong to the same property
        var reservation = await db.Reservations
            .FirstOrDefaultAsync(r => r.Id == req.ReservationId && r.PropertyId == propertyId);
        if (reservation == null)
            return Results.Problem("Reservation not found in this property.", statusCode: 404);

        reservation.SetGroupBooking(groupId);
        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Reservation linked to group.", reservationId = req.ReservationId, groupId });
    }

    private static async Task<IResult> CancelGroupBookingAsync(
        Guid propertyId, Guid groupId,
        [FromBody] CancelGroupBookingRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var group = await db.GroupBookings
            .FirstOrDefaultAsync(g => g.Id == groupId && g.PropertyId == propertyId);
        if (group == null) return Results.NotFound();
        if (group.Status == GroupBookingStatus.Cancelled)
            return Results.Ok(new { message = "Group already cancelled." });

        // Get IDs only (no tracking) to avoid navigation fixup accumulation across iterations.
        var linkedIds = await db.Reservations
            .AsNoTracking()
            .Where(r => r.GroupBookingId == groupId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.CheckedOut)
            .Select(r => r.Id)
            .ToListAsync();

        // Cancel each reservation individually. Clear the change tracker after each save
        // so accumulated navigation-tracked RSH entities from prior iterations don't
        // interfere with the next save's concurrency-token check.
        var cancellationNote = $"Group cancelled: {req.Reason}";
        foreach (var id in linkedIds)
        {
            db.ChangeTracker.Clear();
            var r = await db.Reservations.FindAsync([id]);
            if (r == null) continue;
            r.Cancel(cancellationNote);
            db.ReservationStatusHistories.Add(r.StatusHistories.Last());
            await db.SaveChangesAsync();
        }

        // Reload group after tracker was cleared, then cancel it.
        db.ChangeTracker.Clear();
        group = await db.GroupBookings.FirstOrDefaultAsync(g => g.Id == groupId);
        if (group == null) return Results.NotFound();
        group.Cancel(req.Reason);
        await db.SaveChangesAsync();
        return Results.Ok(new { message = "Group and linked reservations cancelled.", cancelledCount = linkedIds.Count });
    }
}

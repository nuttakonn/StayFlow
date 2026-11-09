using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Domain.Services;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record CreateQuotationRequest(
    Guid GuestId,
    Guid? RoomTypeId,
    string CheckInDate,
    string CheckOutDate,
    int Adults,
    int Children,
    Guid? RatePlanId,
    decimal QuotedRatePerNight,
    decimal TotalAmount,
    string Currency,
    string? ExpiryDate,
    string? Notes);

public record ConvertQuotationRequest(Guid? RoomId);

public record QuotationDto(
    Guid Id,
    Guid PropertyId,
    string QuotationNumber,
    Guid GuestId,
    string? GuestName,
    Guid? RoomTypeId,
    string CheckInDate,
    string CheckOutDate,
    int Adults,
    int Children,
    int Nights,
    Guid? RatePlanId,
    decimal QuotedRatePerNight,
    decimal TotalAmount,
    string Currency,
    string Status,
    string? ExpiryDate,
    string? Notes,
    Guid? ConvertedReservationId,
    DateTimeOffset CreatedAtUtc);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class QuotationEndpoints
{
    public static void MapQuotationEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/quotations")
            .WithTags("Quotations")
            .RequireAuthorization();

        auth.MapGet("/", ListQuotationsAsync);
        auth.MapPost("/", CreateQuotationAsync);
        auth.MapGet("/{id:guid}", GetQuotationAsync);
        auth.MapPost("/{id:guid}/convert", ConvertToReservationAsync);
        auth.MapPost("/{id:guid}/cancel", CancelQuotationAsync);
    }

    private static async Task<IResult> ListQuotationsAsync(
        Guid propertyId,
        [FromQuery] string? status,
        [FromQuery] bool? excludeExpired,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        var query = db.Quotations.Where(q => q.PropertyId == propertyId);

        if (Enum.TryParse<QuotationStatus>(status, ignoreCase: true, out var st))
            query = query.Where(q => q.Status == st);

        var list = await query.OrderByDescending(q => q.CreatedAtUtc).ToListAsync();

        // Filter expiry in C# — EF InMemory does not reliably evaluate DateOnly? expression trees
        if (excludeExpired ?? false)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            list = list.Where(q => !q.ExpiryDate.HasValue || q.ExpiryDate.Value >= today).ToList();
        }
        var guestNames = await LoadGuestNamesAsync(db, list);
        return Results.Ok(new { data = list.Select(q => ToDto(q, guestNames)), total = list.Count });
    }

    private static async Task<IResult> CreateQuotationAsync(
        Guid propertyId,
        [FromBody] CreateQuotationRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();
        if (!await db.Properties.AnyAsync(p => p.Id == propertyId)) return Results.NotFound();

        if (!DateOnly.TryParse(req.CheckInDate, out var checkIn) ||
            !DateOnly.TryParse(req.CheckOutDate, out var checkOut))
            return Results.Problem("Invalid date format. Use yyyy-MM-dd.", statusCode: 400);

        if (checkOut <= checkIn)
            return Results.Problem("Check-out must be after check-in.", statusCode: 400);

        if (!await db.Guests.AnyAsync(g => g.Id == req.GuestId))
            return Results.Problem("Guest not found.", statusCode: 404);

        DateOnly? expiry = null;
        if (req.ExpiryDate != null)
        {
            if (!DateOnly.TryParse(req.ExpiryDate, out var parsedExpiry))
                return Results.Problem("Invalid expiry date format. Use yyyy-MM-dd.", statusCode: 400);
            expiry = parsedExpiry;
        }

        // Generate quotation number: QT-{propertyId_prefix}-{yyyyMMdd}-{seq}
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var prefix = $"QT-{today:yyyyMMdd}";
        var count = await db.Quotations.CountAsync(q => q.PropertyId == propertyId && q.QuotationNumber.StartsWith(prefix));
        var quotationNumber = $"{prefix}-{(count + 1):D4}";

        var quotation = Quotation.Create(
            tenant.OrganizationId.Value, propertyId, quotationNumber,
            req.GuestId, req.RoomTypeId, checkIn, checkOut,
            req.Adults, req.Children, req.RatePlanId,
            req.QuotedRatePerNight, req.TotalAmount, req.Currency,
            expiry, req.Notes);

        db.Quotations.Add(quotation);
        await db.SaveChangesAsync();

        var guestNames = await LoadGuestNamesAsync(db, [quotation]);
        return Results.Created($"/api/v1/properties/{propertyId}/quotations/{quotation.Id}",
            ToDto(quotation, guestNames));
    }

    private static async Task<IResult> GetQuotationAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var q = await db.Quotations.FirstOrDefaultAsync(q => q.Id == id && q.PropertyId == propertyId);
        if (q == null) return Results.NotFound();
        var guestNames = await LoadGuestNamesAsync(db, [q]);
        return Results.Ok(ToDto(q, guestNames));
    }

    private static async Task<IResult> ConvertToReservationAsync(
        Guid propertyId, Guid id,
        [FromBody] ConvertQuotationRequest? req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var q = await db.Quotations.FirstOrDefaultAsync(q => q.Id == id && q.PropertyId == propertyId);
        if (q == null) return Results.NotFound();

        if (q.Status == QuotationStatus.Converted)
            return Results.Problem("Quotation already converted.", statusCode: 409);
        if (q.Status == QuotationStatus.Cancelled)
            return Results.Problem("Cannot convert a cancelled quotation.", statusCode: 409);

        // Check expiry
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (q.ExpiryDate.HasValue && q.ExpiryDate.Value < today)
            return Results.Problem("Quotation has expired.", statusCode: 409);

        // Check availability
        var roomId = req?.RoomId;
        if (roomId.HasValue)
        {
            var overlap = await db.Reservations.AnyAsync(r =>
                r.RoomId == roomId &&
                r.PropertyId == propertyId &&
                r.Status != ReservationStatus.Cancelled &&
                r.Status != ReservationStatus.NoShow &&
                r.CheckInDate < q.CheckOutDate &&
                q.CheckInDate < r.CheckOutDate);
            if (overlap)
                return Results.Problem("Room is not available for the requested dates.", statusCode: 409);
        }

        var reservation = Reservation.Create(
            tenant.OrganizationId.Value, propertyId,
            q.GuestId, q.CheckInDate, q.CheckOutDate,
            q.Adults, q.Children, q.RoomTypeId, roomId,
            q.QuotedRatePerNight, q.TotalAmount, q.Currency,
            ReservationSource.Other, null, $"Converted from quotation {q.QuotationNumber}",
            q.RatePlanId);

        db.Reservations.Add(reservation);
        db.ReservationStatusHistories.Add(reservation.StatusHistories.Last());

        q.MarkConverted(reservation.Id);
        await db.SaveChangesAsync();

        return Results.Ok(new { reservationId = reservation.Id, quotationNumber = q.QuotationNumber });
    }

    private static async Task<IResult> CancelQuotationAsync(
        Guid propertyId, Guid id, StayFlowDbContext db, ITenantContext tenant)
    {
        var q = await db.Quotations.FirstOrDefaultAsync(q => q.Id == id && q.PropertyId == propertyId);
        if (q == null) return Results.NotFound();
        if (q.Status == QuotationStatus.Converted)
            return Results.Problem("Cannot cancel a converted quotation.", statusCode: 409);
        q.Cancel();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    private static async Task<Dictionary<Guid, string>> LoadGuestNamesAsync(
        StayFlowDbContext db, IEnumerable<Quotation> quotations)
    {
        var ids = quotations.Select(q => q.GuestId).Distinct().ToList();
        return await db.Guests
            .Where(g => ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.FullName);
    }

    private static QuotationDto ToDto(Quotation q, Dictionary<Guid, string> guestNames) =>
        new(q.Id, q.PropertyId, q.QuotationNumber, q.GuestId,
            guestNames.GetValueOrDefault(q.GuestId),
            q.RoomTypeId,
            q.CheckInDate.ToString("yyyy-MM-dd"),
            q.CheckOutDate.ToString("yyyy-MM-dd"),
            q.Adults, q.Children, q.Nights,
            q.RatePlanId, q.QuotedRatePerNight, q.TotalAmount, q.Currency,
            q.Status.ToString(),
            q.ExpiryDate?.ToString("yyyy-MM-dd"),
            q.Notes, q.ConvertedReservationId, q.CreatedAtUtc);
}

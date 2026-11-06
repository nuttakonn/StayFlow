using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Parties;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record CreateGuestRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? Notes
);

public record UpdateGuestRequest(
    string FullName,
    string? Email,
    string? Phone,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? Notes
);

public record GuestDto(
    Guid Id,
    Guid OrganizationId,
    string FullName,
    string? Email,
    string? Phone,
    string? NationalId,
    string? PassportNumber,
    string? Nationality,
    string? Notes,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

public record GuestStayDto(
    Guid ReservationId,
    string PropertyName,
    string? RoomTypeName,
    string? RoomNumber,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Nights,
    int Adults,
    int Children,
    decimal TotalAmount,
    string Currency,
    string Status,
    DateTimeOffset CreatedAtUtc
);

public static class GuestEndpoints
{
    public static RouteGroupBuilder MapGuestEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListGuestsAsync)
            .WithName("ListGuests")
            .WithSummary("Search and list guests for the active organization")
            .Produces<IEnumerable<GuestDto>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateGuestAsync)
            .WithName("CreateGuest")
            .WithSummary("Create a new guest profile")
            .Produces<GuestDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetGuestByIdAsync)
            .WithName("GetGuestById")
            .WithSummary("Get guest profile by ID")
            .Produces<GuestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateGuestAsync)
            .WithName("UpdateGuest")
            .WithSummary("Update guest profile")
            .Produces<GuestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/reservations", GetGuestStayHistoryAsync)
            .WithName("GetGuestStayHistory")
            .WithSummary("Get all reservations (stay history) for a guest within the active organization")
            .Produces<IEnumerable<GuestStayDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListGuestsAsync(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        StayFlowDbContext dbContext = default!,
        ITenantContext tenantContext = default!,
        CancellationToken ct = default)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var query = dbContext.Guests
            .Where(g => g.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var lower = search.Trim().ToLowerInvariant();
            query = query.Where(g =>
                g.FullName.ToLower().Contains(lower) ||
                (g.Email != null && g.Email.Contains(lower)) ||
                (g.Phone != null && g.Phone.Contains(lower)) ||
                (g.NationalId != null && g.NationalId.Contains(lower)) ||
                (g.PassportNumber != null && g.PassportNumber.Contains(lower)));
        }

        var total = await query.CountAsync(ct);
        var guests = await query
            .OrderByDescending(g => g.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var dtos = guests.Select(ToDto).ToList();

        return Results.Ok(new
        {
            data = dtos,
            total,
            page,
            pageSize
        });
    }

    private static async Task<IResult> CreateGuestAsync(
        [FromBody] CreateGuestRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.FullName))
            return Results.Problem("Full name is required.", statusCode: StatusCodes.Status400BadRequest);

        // Check email uniqueness within org (if provided)
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = request.Email.Trim().ToLowerInvariant();
            var emailExists = await dbContext.Guests
                .AnyAsync(g => g.OrganizationId == tenantContext.OrganizationId.Value && g.Email == normalizedEmail && g.IsActive, ct);

            if (emailExists)
                return Results.Problem("A guest with this email already exists.", statusCode: StatusCodes.Status400BadRequest);
        }

        var guest = Guest.Create(
            organizationId: tenantContext.OrganizationId.Value,
            fullName: request.FullName,
            email: request.Email,
            phone: request.Phone,
            nationalId: request.NationalId,
            passportNumber: request.PassportNumber,
            nationality: request.Nationality,
            notes: request.Notes);

        dbContext.Guests.Add(guest);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/guests/{guest.Id}", ToDto(guest));
    }

    private static async Task<IResult> GetGuestByIdAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var guest = await dbContext.Guests.FindAsync([id], ct);
        if (guest == null || guest.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        return Results.Ok(ToDto(guest));
    }

    private static async Task<IResult> UpdateGuestAsync(
        Guid id,
        [FromBody] UpdateGuestRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var guest = await dbContext.Guests.FindAsync([id], ct);
        if (guest == null || guest.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.FullName))
            return Results.Problem("Full name is required.", statusCode: StatusCodes.Status400BadRequest);

        guest.UpdateProfile(
            request.FullName,
            request.Email,
            request.Phone,
            request.NationalId,
            request.PassportNumber,
            request.Nationality,
            request.Notes);

        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(ToDto(guest));
    }

    private static async Task<IResult> GetGuestStayHistoryAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var guest = await dbContext.Guests.FindAsync([id], ct);
        if (guest == null || guest.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        var reservations = await dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.GuestId == id && r.OrganizationId == tenantContext.OrganizationId.Value)
            .OrderByDescending(r => r.CheckInDate)
            .ToListAsync(ct);

        var roomTypeIds = reservations.Where(r => r.RoomTypeId.HasValue).Select(r => r.RoomTypeId!.Value).Distinct().ToList();
        var roomTypes = await dbContext.RoomTypes.AsNoTracking()
            .Where(rt => roomTypeIds.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt.Name, ct);

        var roomIds = reservations.Where(r => r.RoomId.HasValue).Select(r => r.RoomId!.Value).Distinct().ToList();
        var rooms = await dbContext.Rooms.AsNoTracking()
            .Where(r => roomIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, r => r.RoomNumber, ct);

        var propertyIds = reservations.Select(r => r.PropertyId).Distinct().ToList();
        var properties = await dbContext.Properties.AsNoTracking()
            .Where(p => propertyIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var stays = reservations.Select(r => new GuestStayDto(
            r.Id,
            properties.TryGetValue(r.PropertyId, out var pn) ? pn : r.PropertyId.ToString(),
            r.RoomTypeId.HasValue && roomTypes.TryGetValue(r.RoomTypeId.Value, out var rtn) ? rtn : null,
            r.RoomId.HasValue && rooms.TryGetValue(r.RoomId.Value, out var rn) ? rn : null,
            r.CheckInDate,
            r.CheckOutDate,
            r.NumberOfNights,
            r.Adults,
            r.Children,
            r.TotalAmount,
            r.Currency,
            r.Status.ToString(),
            r.CreatedAtUtc
        )).ToList();

        return Results.Ok(stays);
    }

    private static GuestDto ToDto(Guest g) => new(
        g.Id,
        g.OrganizationId,
        g.FullName,
        g.Email,
        g.Phone,
        g.NationalId,
        g.PassportNumber,
        g.Nationality,
        g.Notes,
        g.IsActive,
        g.CreatedAtUtc,
        g.UpdatedAtUtc);
}

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record DashboardArrivalDto(
    Guid ReservationId,
    string? GuestName,
    string? RoomNumber,
    string? RoomTypeName,
    int Nights,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    string Status
);

public record RoomStatusCountDto(
    string Status,
    int Count
);

public record PropertyDashboardDto(
    // Today's operations
    int TodayArrivals,
    int TodayDepartures,
    int InHouseGuests,
    // Room inventory
    int TotalActiveRooms,
    int OccupiedRooms,
    int AvailableRooms,
    decimal OccupancyRate,
    IReadOnlyCollection<RoomStatusCountDto> RoomStatusBreakdown,
    // Financial snapshot
    int OpenFoliosCount,
    decimal TotalOutstandingBalance,
    decimal TodayCollected,
    string Currency,
    // Arrival / departure lists (top 10 each)
    IReadOnlyCollection<DashboardArrivalDto> TodayArrivalList,
    IReadOnlyCollection<DashboardArrivalDto> TodayDepartureList
);

public static class DashboardEndpoints
{
    public static RouteGroupBuilder MapDashboardEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetDashboardAsync)
            .WithName("GetPropertyDashboard")
            .WithSummary("Get operational dashboard snapshot for a property (arrivals, occupancy, revenue)")
            .Produces<PropertyDashboardDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetDashboardAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await dbContext.Properties
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);

        if (property == null) return Results.NotFound();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currency = property.DefaultCurrency;

        // ── Rooms ─────────────────────────────────────────────────────
        var rooms = await dbContext.Rooms
            .AsNoTracking()
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .ToListAsync(ct);

        var totalActiveRooms = rooms.Count;

        var roomStatusBreakdown = rooms
            .GroupBy(r => r.OperationalStatus.ToString())
            .Select(g => new RoomStatusCountDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ToList();

        // ── Reservations ───────────────────────────────────────────────
        var activeReservations = await dbContext.Reservations
            .AsNoTracking()
            .Where(r => r.PropertyId == propertyId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow)
            .ToListAsync(ct);

        var inHouseGuests = activeReservations.Count(r => r.Status == ReservationStatus.CheckedIn);
        var occupiedRooms = inHouseGuests;
        var availableRooms = Math.Max(0, totalActiveRooms - occupiedRooms);
        var occupancyRate = totalActiveRooms > 0
            ? Math.Round((decimal)occupiedRooms / totalActiveRooms * 100m, 1)
            : 0m;

        // Today's arrivals: CheckInDate == today, status is Confirmed or CheckedIn
        var todayArrivalReservations = activeReservations
            .Where(r => r.CheckInDate == today &&
                        (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.CheckedIn))
            .ToList();

        // Today's departures: CheckOutDate == today, status is CheckedIn
        var todayDepartureReservations = activeReservations
            .Where(r => r.CheckOutDate == today && r.Status == ReservationStatus.CheckedIn)
            .ToList();

        // Load room numbers for display
        var roomDict = rooms.ToDictionary(r => r.Id, r => r.RoomNumber);

        // Load room types for display
        var roomTypeIds = activeReservations.Where(r => r.RoomTypeId.HasValue).Select(r => r.RoomTypeId!.Value).Distinct().ToList();
        var roomTypes = await dbContext.RoomTypes
            .AsNoTracking()
            .Where(rt => roomTypeIds.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt.Name, ct);

        // Load guest names
        var guestIds = activeReservations.Select(r => r.GuestId).Distinct().ToList();
        var guests = await dbContext.Guests
            .AsNoTracking()
            .Where(g => guestIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.FullName, ct);

        static DashboardArrivalDto ToArrivalDto(
            StayFlow.BuildingBlocks.Domain.Reservations.Reservation r,
            Dictionary<Guid, string> roomDict,
            Dictionary<Guid, string> roomTypes,
            Dictionary<Guid, string> guests)
        {
            guests.TryGetValue(r.GuestId, out var guestName);
            var roomNum = r.RoomId.HasValue && roomDict.TryGetValue(r.RoomId.Value, out var rn) ? rn : null;
            var roomType = r.RoomTypeId.HasValue && roomTypes.TryGetValue(r.RoomTypeId.Value, out var rt) ? rt : null;

            return new DashboardArrivalDto(
                r.Id,
                guestName,
                roomNum,
                roomType,
                r.NumberOfNights,
                r.CheckInDate,
                r.CheckOutDate,
                r.Status.ToString());
        }

        var arrivalList = todayArrivalReservations
            .Take(10)
            .Select(r => ToArrivalDto(r, roomDict, roomTypes, guests))
            .ToList();

        var departureList = todayDepartureReservations
            .Take(10)
            .Select(r => ToArrivalDto(r, roomDict, roomTypes, guests))
            .ToList();

        // ── Billing ───────────────────────────────────────────────────
        var folios = await dbContext.Folios
            .AsNoTracking()
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .Where(f => f.PropertyId == propertyId)
            .ToListAsync(ct);

        var openFolios = folios.Where(f => f.Status == FolioStatus.Open).ToList();
        var openFoliosCount = openFolios.Count;
        var totalOutstandingBalance = openFolios.Sum(f => f.Balance);

        var startOfTodayUtc = DateTime.UtcNow.Date;
        var todayCollected = folios
            .SelectMany(f => f.Payments)
            .Where(p => p.Status == PaymentStatus.Completed &&
                        p.CreatedAtUtc >= startOfTodayUtc)
            .Sum(p => p.Amount);

        return Results.Ok(new PropertyDashboardDto(
            todayArrivalReservations.Count,
            todayDepartureReservations.Count,
            inHouseGuests,
            totalActiveRooms,
            occupiedRooms,
            availableRooms,
            occupancyRate,
            roomStatusBreakdown,
            openFoliosCount,
            totalOutstandingBalance,
            todayCollected,
            currency,
            arrivalList,
            departureList
        ));
    }
}

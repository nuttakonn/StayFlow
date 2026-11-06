using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record RoomMoveRequest(
    Guid NewRoomId,
    string Reason
);

public record SetOutOfServiceRequest(
    string Status, // OutOfService | Maintenance
    string? Reason
);

public record ReturnToServiceRequest(
    string TargetStatus, // Clean | Dirty | Inspected
    string? Notes
);

public record StayOperationItemDto(
    Guid ReservationId,
    Guid GuestId,
    string GuestName,
    string? GuestPhone,
    string? GuestEmail,
    Guid? RoomId,
    string? RoomNumber,
    string? RoomTypeName,
    string CheckInDate,
    string CheckOutDate,
    int NumberOfNights,
    int Adults,
    int Children,
    string Status,
    decimal TotalAmount,
    string Currency,
    DateTimeOffset? CheckedInAtUtc,
    DateTimeOffset? CheckedOutAtUtc
);

public record OperationsDashboardSummaryDto(
    int ExpectedArrivals,
    int CompletedArrivals,
    int ExpectedDepartures,
    int CompletedDepartures,
    int InHouseGuests,
    int CleanRooms,
    int DirtyRooms,
    int CleaningRooms,
    int InspectedRooms,
    int OutOfServiceRooms,
    int MaintenanceRooms
);

public static class StayOperationsEndpoints
{
    public static RouteGroupBuilder MapStayOperationsEndpoints(this RouteGroupBuilder group)
    {
        // Operations Dashboard
        group.MapGet("/dashboard", GetOperationsDashboardAsync)
            .WithName("GetOperationsDashboard")
            .WithSummary("Get daily front-desk and housekeeping operational stats")
            .Produces<OperationsDashboardSummaryDto>(StatusCodes.Status200OK);

        // Daily lists
        group.MapGet("/arrivals", GetExpectedArrivalsAsync)
            .WithName("GetExpectedArrivals")
            .WithSummary("Get list of expected arrivals for a business date")
            .Produces<IEnumerable<StayOperationItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/departures", GetExpectedDeparturesAsync)
            .WithName("GetExpectedDepartures")
            .WithSummary("Get list of expected departures for a business date")
            .Produces<IEnumerable<StayOperationItemDto>>(StatusCodes.Status200OK);

        group.MapGet("/in-house", GetInHouseStaysAsync)
            .WithName("GetInHouseStays")
            .WithSummary("Get list of all currently checked-in guests")
            .Produces<IEnumerable<StayOperationItemDto>>(StatusCodes.Status200OK);

        // Room Move
        group.MapPost("/reservations/{reservationId:guid}/room-move", MoveRoomAsync)
            .WithName("MoveRoom")
            .WithSummary("Move an in-house guest to another available room")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        // Out-of-service / Return-to-service
        group.MapPost("/rooms/{roomId:guid}/out-of-service", SetRoomOutOfServiceAsync)
            .WithName("SetRoomOutOfService")
            .WithSummary("Take a room out of service or set to maintenance")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/rooms/{roomId:guid}/return-to-service", ReturnRoomToServiceAsync)
            .WithName("ReturnRoomToService")
            .WithSummary("Return an out-of-service or maintenance room back to active inventory")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // Night Audit
        group.MapPost("/night-audit", RunNightAuditAsync)
            .WithName("RunNightAudit")
            .WithSummary("Post nightly room charges to all in-house folios for a business date (idempotent)")
            .Produces<NightAuditResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/night-audit/preview", PreviewNightAuditAsync)
            .WithName("PreviewNightAudit")
            .WithSummary("Preview which in-house guests will receive a room charge for a given business date")
            .Produces<NightAuditResultDto>(StatusCodes.Status200OK);

        return group;
    }

    private static async Task<IResult> GetOperationsDashboardAsync(
        Guid propertyId,
        [FromQuery] string? date,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var businessDate = DateOnly.TryParse(date, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        // Reservations queries
        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                       (r.CheckInDate == businessDate || r.CheckOutDate == businessDate || r.Status == ReservationStatus.CheckedIn))
            .ToListAsync(ct);

        var expectedArrivals = reservations.Count(r => r.CheckInDate == businessDate && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Pending));
        var completedArrivals = reservations.Count(r => r.CheckInDate == businessDate && r.Status == ReservationStatus.CheckedIn);
        var expectedDepartures = reservations.Count(r => r.CheckOutDate == businessDate && r.Status == ReservationStatus.CheckedIn);
        var completedDepartures = reservations.Count(r => r.CheckOutDate == businessDate && r.Status == ReservationStatus.CheckedOut);
        var inHouse = reservations.Count(r => r.Status == ReservationStatus.CheckedIn);

        // Rooms housekeeping queries
        var rooms = await dbContext.Rooms
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .Select(r => r.OperationalStatus)
            .ToListAsync(ct);

        var summary = new OperationsDashboardSummaryDto(
            ExpectedArrivals: expectedArrivals,
            CompletedArrivals: completedArrivals,
            ExpectedDepartures: expectedDepartures,
            CompletedDepartures: completedDepartures,
            InHouseGuests: inHouse,
            CleanRooms: rooms.Count(s => s == RoomOperationalStatus.Clean),
            DirtyRooms: rooms.Count(s => s == RoomOperationalStatus.Dirty),
            CleaningRooms: rooms.Count(s => s == RoomOperationalStatus.Cleaning),
            InspectedRooms: rooms.Count(s => s == RoomOperationalStatus.Inspected),
            OutOfServiceRooms: rooms.Count(s => s == RoomOperationalStatus.OutOfService),
            MaintenanceRooms: rooms.Count(s => s == RoomOperationalStatus.Maintenance)
        );

        return Results.Ok(summary);
    }

    private static async Task<IResult> GetExpectedArrivalsAsync(
        Guid propertyId,
        [FromQuery] string? date,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var businessDate = DateOnly.TryParse(date, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                        r.CheckInDate == businessDate &&
                        (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Pending || r.Status == ReservationStatus.CheckedIn))
            .OrderBy(r => r.Status)
            .ToListAsync(ct);

        return Results.Ok(await MapToOperationItemsAsync(reservations, dbContext, ct));
    }

    private static async Task<IResult> GetExpectedDeparturesAsync(
        Guid propertyId,
        [FromQuery] string? date,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var businessDate = DateOnly.TryParse(date, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                        r.CheckOutDate == businessDate &&
                        (r.Status == ReservationStatus.CheckedIn || r.Status == ReservationStatus.CheckedOut))
            .OrderBy(r => r.Status)
            .ToListAsync(ct);

        return Results.Ok(await MapToOperationItemsAsync(reservations, dbContext, ct));
    }

    private static async Task<IResult> GetInHouseStaysAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId && r.Status == ReservationStatus.CheckedIn)
            .OrderBy(r => r.CheckOutDate)
            .ToListAsync(ct);

        return Results.Ok(await MapToOperationItemsAsync(reservations, dbContext, ct));
    }

    private static async Task<IResult> MoveRoomAsync(
        Guid propertyId,
        Guid reservationId,
        [FromBody] RoomMoveRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.Problem("A reason for moving the room is required.", statusCode: StatusCodes.Status400BadRequest);

        var reservation = await dbContext.Reservations.FindAsync([reservationId], ct);
        if (reservation == null || reservation.PropertyId != propertyId)
            return Results.NotFound();

        if (reservation.Status != ReservationStatus.CheckedIn)
            return Results.Problem($"Cannot move room for a reservation with status '{reservation.Status}'. Guest must be checked-in.", statusCode: StatusCodes.Status422UnprocessableEntity);

        if (reservation.RoomId == request.NewRoomId)
            return Results.Problem("The target room is already assigned to this reservation.", statusCode: StatusCodes.Status400BadRequest);

        var newRoom = await dbContext.Rooms.FindAsync([request.NewRoomId], ct);
        if (newRoom == null || newRoom.PropertyId != propertyId)
            return Results.Problem("Target room not found in this property.", statusCode: StatusCodes.Status400BadRequest);

        if (!newRoom.IsActive)
            return Results.Problem("Target room is not active.", statusCode: StatusCodes.Status400BadRequest);

        if (newRoom.OperationalStatus is RoomOperationalStatus.OutOfService or RoomOperationalStatus.Maintenance)
            return Results.Problem($"Target room is currently {newRoom.OperationalStatus} and cannot be assigned.", statusCode: StatusCodes.Status409Conflict);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Check for conflicting reservations on the target room for the remaining stay period
        var hasConflict = await dbContext.Reservations
            .AnyAsync(r =>
                r.Id != reservationId &&
                r.RoomId == request.NewRoomId &&
                r.PropertyId == propertyId &&
                r.Status != ReservationStatus.Cancelled &&
                r.Status != ReservationStatus.NoShow &&
                r.CheckInDate < reservation.CheckOutDate &&
                today < r.CheckOutDate,
                ct);

        if (hasConflict)
            return Results.Problem("Target room is not available for the remaining stay period.", statusCode: StatusCodes.Status409Conflict);

        // Perform Room Move
        var oldRoomId = reservation.RoomId;
        Room? oldRoom = oldRoomId.HasValue ? await dbContext.Rooms.FindAsync([oldRoomId.Value], ct) : null;

        reservation.MoveRoom(request.NewRoomId, oldRoom?.RoomNumber, newRoom.RoomNumber, request.Reason);
        dbContext.ReservationStatusHistories.Add(reservation.StatusHistories.Last());

        // Update old room to Dirty (needs cleaning after move)
        if (oldRoom != null)
        {
            var history = oldRoom.UpdateOperationalStatus(
                RoomOperationalStatus.Dirty,
                $"Guest moved to Room {newRoom.RoomNumber}. Reason: {request.Reason}",
                tenantContext.UserId,
                tenantContext.UserEmail);
            dbContext.RoomStatusHistories.Add(history);
        }

        await dbContext.SaveChangesAsync(ct);

        var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
        var roomType = await dbContext.RoomTypes.FindAsync([newRoom.RoomTypeId], ct);
        var histories = await dbContext.ReservationStatusHistories
            .Where(h => h.ReservationId == reservationId)
            .OrderBy(h => h.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(new ReservationDto(
            reservation.Id,
            reservation.OrganizationId,
            reservation.PropertyId,
            reservation.GuestId,
            guest?.FullName ?? "Unknown",
            guest?.Email,
            guest?.Phone,
            reservation.RoomTypeId,
            roomType?.Name,
            reservation.RoomId,
            newRoom.RoomNumber,
            reservation.CheckInDate.ToString("yyyy-MM-dd"),
            reservation.CheckOutDate.ToString("yyyy-MM-dd"),
            reservation.NumberOfNights,
            reservation.Adults,
            reservation.Children,
            reservation.QuotedRate,
            reservation.TotalAmount,
            reservation.Currency,
            reservation.Status.ToString(),
            reservation.Source.ToString(),
            reservation.SpecialRequests,
            reservation.Notes,
            reservation.CancellationReason,
            reservation.CancelledAtUtc,
            reservation.CheckedInAtUtc,
            reservation.CheckedOutAtUtc,
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc,
            histories.Select(h => new ReservationStatusHistoryDto(
                h.Id,
                h.FromStatus?.ToString(),
                h.ToStatus.ToString(),
                h.Note,
                h.CreatedAtUtc)).ToList()
        ));
    }

    private static async Task<IResult> SetRoomOutOfServiceAsync(
        Guid propertyId,
        Guid roomId,
        [FromBody] SetOutOfServiceRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var room = await dbContext.Rooms.FindAsync([roomId], ct);
        if (room == null || room.PropertyId != propertyId)
            return Results.NotFound();

        // Check if there is an active in-house reservation in this room
        var isOccupied = await dbContext.Reservations
            .AnyAsync(r => r.RoomId == roomId && r.Status == ReservationStatus.CheckedIn, ct);

        if (isOccupied)
            return Results.Problem("Cannot set room out of service while a guest is currently checked in. Please move the guest first.", statusCode: StatusCodes.Status409Conflict);

        var targetStatus = request.Status.Equals("Maintenance", StringComparison.OrdinalIgnoreCase)
            ? RoomOperationalStatus.Maintenance
            : RoomOperationalStatus.OutOfService;

        var history = room.UpdateOperationalStatus(
            targetStatus,
            request.Reason ?? "Room taken out of service",
            tenantContext.UserId,
            tenantContext.UserEmail);

        dbContext.RoomStatusHistories.Add(history);
        await dbContext.SaveChangesAsync(ct);

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Ok(new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc));
    }

    private static async Task<IResult> ReturnRoomToServiceAsync(
        Guid propertyId,
        Guid roomId,
        [FromBody] ReturnToServiceRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var room = await dbContext.Rooms.FindAsync([roomId], ct);
        if (room == null || room.PropertyId != propertyId)
            return Results.NotFound();

        if (room.OperationalStatus != RoomOperationalStatus.OutOfService && room.OperationalStatus != RoomOperationalStatus.Maintenance)
            return Results.Problem("Room is not currently out of service or in maintenance.", statusCode: StatusCodes.Status400BadRequest);

        var targetStatus = request.TargetStatus.ToLowerInvariant() switch
        {
            "clean" => RoomOperationalStatus.Clean,
            "inspected" => RoomOperationalStatus.Inspected,
            _ => RoomOperationalStatus.Dirty
        };

        var history = room.UpdateOperationalStatus(
            targetStatus,
            request.Notes ?? "Room returned to service",
            tenantContext.UserId,
            tenantContext.UserEmail);

        dbContext.RoomStatusHistories.Add(history);
        await dbContext.SaveChangesAsync(ct);

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Ok(new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc));
    }

    private static async Task<List<StayOperationItemDto>> MapToOperationItemsAsync(
        List<Reservation> reservations,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var guestIds = reservations.Select(r => r.GuestId).Distinct().ToList();
        var guests = await dbContext.Guests.Where(g => guestIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        var roomIds = reservations.Where(r => r.RoomId.HasValue).Select(r => r.RoomId!.Value).Distinct().ToList();
        var rooms = await dbContext.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var roomTypeIds = reservations.Where(r => r.RoomTypeId.HasValue).Select(r => r.RoomTypeId!.Value).Distinct().ToList();
        var roomTypes = await dbContext.RoomTypes.Where(rt => roomTypeIds.Contains(rt.Id)).ToDictionaryAsync(rt => rt.Id, ct);

        return reservations.Select(r =>
        {
            guests.TryGetValue(r.GuestId, out var guest);
            var room = r.RoomId.HasValue ? rooms.GetValueOrDefault(r.RoomId.Value) : null;
            var roomType = r.RoomTypeId.HasValue ? roomTypes.GetValueOrDefault(r.RoomTypeId.Value) : null;

            return new StayOperationItemDto(
                r.Id,
                r.GuestId,
                guest?.FullName ?? "Unknown Guest",
                guest?.Phone,
                guest?.Email,
                r.RoomId,
                room?.RoomNumber,
                roomType?.Name,
                r.CheckInDate.ToString("yyyy-MM-dd"),
                r.CheckOutDate.ToString("yyyy-MM-dd"),
                r.NumberOfNights,
                r.Adults,
                r.Children,
                r.Status.ToString(),
                r.TotalAmount,
                r.Currency,
                r.CheckedInAtUtc,
                r.CheckedOutAtUtc
            );
        }).ToList();
    }

    // ── Night Audit ──────────────────────────────────────────────────────────

    private static async Task<IResult> PreviewNightAuditAsync(
        Guid propertyId,
        [FromQuery] string? businessDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        var (date, err) = ResolveBusinessDate(businessDate);
        if (err != null) return Results.Problem(err, statusCode: StatusCodes.Status400BadRequest);

        return Results.Ok(await BuildAuditResult(propertyId, tenantContext.OrganizationId.Value, date, dbContext, dryRun: true, ct));
    }

    private static async Task<IResult> RunNightAuditAsync(
        Guid propertyId,
        [FromBody] NightAuditRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        var (date, err) = ResolveBusinessDate(request.BusinessDate);
        if (err != null) return Results.Problem(err, statusCode: StatusCodes.Status400BadRequest);

        var result = await BuildAuditResult(propertyId, tenantContext.OrganizationId.Value, date, dbContext, dryRun: false, ct);
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(result);
    }

    private static (DateOnly date, string? error) ResolveBusinessDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (DateOnly.FromDateTime(DateTime.UtcNow), null);
        if (DateOnly.TryParse(raw, out var d))
            return (d, null);
        return (default, $"Invalid date '{raw}'. Use yyyy-MM-dd.");
    }

    private static async Task<NightAuditResultDto> BuildAuditResult(
        Guid propertyId,
        Guid orgId,
        DateOnly businessDate,
        StayFlowDbContext dbContext,
        bool dryRun,
        CancellationToken ct)
    {
        // All checked-in reservations that occupy this business date night
        var inHouse = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId
                     && r.OrganizationId == orgId
                     && r.Status == ReservationStatus.CheckedIn
                     && r.CheckInDate <= businessDate
                     && r.CheckOutDate > businessDate)
            .ToListAsync(ct);

        if (inHouse.Count == 0)
            return new NightAuditResultDto(businessDate.ToString("yyyy-MM-dd"), 0, 0, 0m, "THB", []);

        var reservationIds = inHouse.Select(r => r.Id).ToList();

        // Load open folios for these reservations (with items for idempotency check)
        var folios = await dbContext.Folios
            .Include(f => f.Items)
            .Where(f => f.PropertyId == propertyId
                     && f.OrganizationId == orgId
                     && reservationIds.Contains(f.ReservationId)
                     && f.Status == FolioStatus.Open)
            .ToListAsync(ct);

        var folioByReservation = folios.ToDictionary(f => f.ReservationId);

        // Load room types for rates
        var roomTypeIds = inHouse.Where(r => r.RoomTypeId.HasValue).Select(r => r.RoomTypeId!.Value).Distinct().ToList();
        var roomTypes = await dbContext.RoomTypes.AsNoTracking()
            .Where(rt => roomTypeIds.Contains(rt.Id))
            .ToDictionaryAsync(rt => rt.Id, rt => rt, ct);

        var guestIds = inHouse.Select(r => r.GuestId).Distinct().ToList();
        var guests = await dbContext.Guests.AsNoTracking()
            .Where(g => guestIds.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.FullName, ct);

        var dateLabel = businessDate.ToString("yyyy-MM-dd");
        var chargeDescription = $"ค่าห้องพัก {dateLabel}"; // "Room charge YYYY-MM-DD"

        var details = new List<NightAuditLineDto>();
        int posted = 0, skipped = 0;
        decimal totalPosted = 0m;
        string currency = "THB";

        foreach (var res in inHouse)
        {
            var guestName = guests.TryGetValue(res.GuestId, out var gn) ? gn : "Guest";
            var rate = res.RoomTypeId.HasValue && roomTypes.TryGetValue(res.RoomTypeId.Value, out var rt)
                ? rt.BaseRate : 0m;
            currency = res.Currency;

            // Idempotency: skip if folio already has a charge with this exact date description
            if (folioByReservation.TryGetValue(res.Id, out var folio))
            {
                var alreadyPosted = folio.Items.Any(i => i.Description == chargeDescription);
                if (alreadyPosted)
                {
                    skipped++;
                    details.Add(new NightAuditLineDto(res.Id, guestName, rate, currency, "skipped"));
                    continue;
                }

                if (!dryRun)
                    folio.AddCharge(FolioItemType.RoomCharge, chargeDescription, rate, 1);

                posted++;
                totalPosted += rate;
                details.Add(new NightAuditLineDto(res.Id, guestName, rate, currency, "posted"));
            }
            else
            {
                // No open folio — create one and post the charge
                if (!dryRun)
                {
                    var folioNumber = $"F-{res.Id.ToString()[..8].ToUpper()}-NA";
                    var newFolio = Folio.Create(orgId, propertyId, res.Id, folioNumber, res.Currency);
                    newFolio.AddCharge(FolioItemType.RoomCharge, chargeDescription, rate, 1);
                    dbContext.Folios.Add(newFolio);
                }

                posted++;
                totalPosted += rate;
                details.Add(new NightAuditLineDto(res.Id, guestName, rate, currency, "posted"));
            }
        }

        return new NightAuditResultDto(dateLabel, posted, skipped, totalPosted, currency, details);
    }
}

// ── Night Audit DTOs ──────────────────────────────────────────────────────────

public record NightAuditRequest(string? BusinessDate);

public record NightAuditLineDto(
    Guid ReservationId,
    string GuestName,
    decimal RoomRate,
    string Currency,
    string Result  // "posted" | "skipped"
);

public record NightAuditResultDto(
    string BusinessDate,
    int PostedCount,
    int SkippedCount,
    decimal TotalPosted,
    string Currency,
    List<NightAuditLineDto> Lines
);

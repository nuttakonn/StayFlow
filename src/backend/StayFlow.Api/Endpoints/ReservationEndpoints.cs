using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.ChannelManager;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record CreateReservationRequest(
    Guid PropertyId,
    Guid GuestId,
    string CheckInDate,   // YYYY-MM-DD
    string CheckOutDate,  // YYYY-MM-DD
    int Adults,
    int Children,
    Guid? RoomTypeId,
    Guid? RoomId,
    decimal QuotedRate,
    decimal TotalAmount,
    string Currency,
    string Source,        // WalkIn | Phone | Email | Online | OTA | Other
    string? SpecialRequests,
    string? Notes
);

public record UpdateReservationRequest(
    string CheckInDate,
    string CheckOutDate,
    int Adults,
    int Children,
    Guid? RoomTypeId,
    decimal QuotedRate,
    decimal TotalAmount,
    string? SpecialRequests,
    string? Notes
);

public record AssignRoomRequest(Guid RoomId);
public record CancelReservationRequest(string Reason);

public record ReservationStatusHistoryDto(
    Guid Id,
    string? FromStatus,
    string ToStatus,
    string? Note,
    DateTimeOffset CreatedAtUtc
);

public record ReservationDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    Guid GuestId,
    string GuestName,
    string? GuestEmail,
    string? GuestPhone,
    Guid? RoomTypeId,
    string? RoomTypeName,
    Guid? RoomId,
    string? RoomNumber,
    string CheckInDate,
    string CheckOutDate,
    int NumberOfNights,
    int Adults,
    int Children,
    decimal QuotedRate,
    decimal TotalAmount,
    string Currency,
    string Status,
    string Source,
    string? SpecialRequests,
    string? Notes,
    string? CancellationReason,
    DateTimeOffset? CancelledAtUtc,
    DateTimeOffset? CheckedInAtUtc,
    DateTimeOffset? CheckedOutAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    IReadOnlyCollection<ReservationStatusHistoryDto> Timeline
);

public record AvailabilityRoomTypeDto(
    Guid RoomTypeId,
    string Name,
    string Code,
    decimal BaseRate,
    int StandardOccupancy,
    int MaxOccupancy,
    int AvailableRooms,
    IReadOnlyCollection<AvailableRoomDto> Rooms
);

public record AvailableRoomDto(
    Guid RoomId,
    string RoomNumber,
    string? DisplayName,
    string OperationalStatus
);

// ── Endpoint Map ──────────────────────────────────────────────────────
public static class ReservationEndpoints
{
    public static RouteGroupBuilder MapReservationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListReservationsAsync)
            .WithName("ListReservations")
            .WithSummary("List reservations with optional filters")
            .Produces<IEnumerable<ReservationDto>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateReservationAsync)
            .WithName("CreateReservation")
            .WithSummary("Create a new reservation (optionally with room assignment)")
            .Produces<ReservationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetReservationByIdAsync)
            .WithName("GetReservationById")
            .WithSummary("Get reservation detail with timeline")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateReservationAsync)
            .WithName("UpdateReservation")
            .WithSummary("Update reservation dates, guests, and room type")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/confirm", ConfirmReservationAsync)
            .WithName("ConfirmReservation")
            .WithSummary("Confirm a pending reservation")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/assign-room", AssignRoomAsync)
            .WithName("AssignRoom")
            .WithSummary("Assign a specific room to a reservation")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/check-in", CheckInAsync)
            .WithName("CheckIn")
            .WithSummary("Check in a confirmed reservation")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/check-out", CheckOutAsync)
            .WithName("CheckOut")
            .WithSummary("Check out a checked-in reservation")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/cancel", CancelReservationAsync)
            .WithName("CancelReservation")
            .WithSummary("Cancel a reservation")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/{id:guid}/no-show", MarkNoShowAsync)
            .WithName("MarkNoShow")
            .WithSummary("Mark a confirmed reservation as no-show")
            .Produces<ReservationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return group;
    }

    // ── Availability ──────────────────────────────────────────────────────
    public static RouteGroupBuilder MapAvailabilityEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/{propertyId:guid}/availability", GetAvailabilityAsync)
            .WithName("GetAvailability")
            .WithSummary("Get available room types and rooms for a date range")
            .Produces<IEnumerable<AvailabilityRoomTypeDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    // ── Handlers ──────────────────────────────────────────────────────────

    private static async Task<IResult> ListReservationsAsync(
        [FromQuery] Guid? propertyId,
        [FromQuery] string? status,
        [FromQuery] string? checkInDate,
        [FromQuery] string? guestName,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        StayFlowDbContext dbContext = default!,
        ITenantContext tenantContext = default!,
        CancellationToken ct = default)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var query = dbContext.Reservations.AsQueryable();

        if (propertyId.HasValue)
            query = query.Where(r => r.PropertyId == propertyId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<ReservationStatus>(status, true, out var parsedStatus))
            query = query.Where(r => r.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(checkInDate) && DateOnly.TryParse(checkInDate, out var parsedDate))
            query = query.Where(r => r.CheckInDate == parsedDate);

        var total = await query.CountAsync(ct);

        var reservations = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var guestIds = reservations.Select(r => r.GuestId).Distinct().ToList();
        var guests = await dbContext.Guests.Where(g => guestIds.Contains(g.Id)).ToDictionaryAsync(g => g.Id, ct);

        var roomIds = reservations.Where(r => r.RoomId.HasValue).Select(r => r.RoomId!.Value).Distinct().ToList();
        var rooms = await dbContext.Rooms.Where(r => roomIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, ct);

        var roomTypeIds = reservations.Where(r => r.RoomTypeId.HasValue).Select(r => r.RoomTypeId!.Value).Distinct().ToList();
        var roomTypes = await dbContext.RoomTypes.Where(rt => roomTypeIds.Contains(rt.Id)).ToDictionaryAsync(rt => rt.Id, ct);

        var dtos = reservations.Select(r =>
        {
            guests.TryGetValue(r.GuestId, out var guest);
            var room = r.RoomId.HasValue ? rooms.GetValueOrDefault(r.RoomId.Value) : null;
            var roomType = r.RoomTypeId.HasValue ? roomTypes.GetValueOrDefault(r.RoomTypeId.Value) : null;
            return ToDto(r, guest, room, roomType, []);
        }).ToList();

        return Results.Ok(new { data = dtos, total, page, pageSize });
    }

    private static async Task<IResult> CreateReservationAsync(
        [FromBody] CreateReservationRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        if (!DateOnly.TryParse(request.CheckInDate, out var checkIn))
            return Results.Problem("Invalid check-in date format. Use YYYY-MM-DD.", statusCode: StatusCodes.Status400BadRequest);

        if (!DateOnly.TryParse(request.CheckOutDate, out var checkOut))
            return Results.Problem("Invalid check-out date format. Use YYYY-MM-DD.", statusCode: StatusCodes.Status400BadRequest);

        if (checkOut <= checkIn)
            return Results.Problem("Check-out date must be after check-in date.", statusCode: StatusCodes.Status400BadRequest);

        // Validate property belongs to org
        var property = await dbContext.Properties.FindAsync([request.PropertyId], ct);
        if (property == null || property.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.Problem("Property not found.", statusCode: StatusCodes.Status400BadRequest);

        // Validate guest belongs to org
        var guest = await dbContext.Guests.FindAsync([request.GuestId], ct);
        if (guest == null || guest.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.Problem("Guest not found.", statusCode: StatusCodes.Status400BadRequest);

        // If room specified, validate room before entering the transaction
        if (request.RoomId.HasValue)
        {
            var room = await dbContext.Rooms.FindAsync([request.RoomId.Value], ct);
            if (room == null || room.PropertyId != request.PropertyId)
                return Results.Problem("Room not found in this property.", statusCode: StatusCodes.Status400BadRequest);
            if (!room.IsActive)
                return Results.Problem("Room is not active.", statusCode: StatusCodes.Status400BadRequest);
            if (room.OperationalStatus is RoomOperationalStatus.OutOfService or RoomOperationalStatus.Maintenance)
                return Results.Problem($"Room is currently {room.OperationalStatus} and cannot be assigned.", statusCode: StatusCodes.Status409Conflict);
        }

        if (!Enum.TryParse<ReservationSource>(request.Source, true, out var source))
            source = ReservationSource.Phone;

        // Wrap availability check + insert in a serializable transaction.
        // On PostgreSQL this prevents phantom-read races; the exclusion constraint is the last-line defense.
        // On InMemory (tests) this is a no-op but the AnyAsync check still prevents sequential conflicts.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                if (request.RoomId.HasValue)
                {
                    var conflict = await dbContext.Reservations.AnyAsync(r =>
                        r.RoomId == request.RoomId.Value &&
                        r.PropertyId == request.PropertyId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow &&
                        r.CheckInDate < checkOut &&
                        checkIn < r.CheckOutDate, ct);

                    if (conflict)
                        return Results.Problem(
                            "The room is not available for the requested dates.",
                            statusCode: StatusCodes.Status409Conflict);
                }

                var reservation = Reservation.Create(
                    organizationId: tenantContext.OrganizationId.Value,
                    propertyId: request.PropertyId,
                    guestId: request.GuestId,
                    checkInDate: checkIn,
                    checkOutDate: checkOut,
                    adults: request.Adults,
                    children: request.Children,
                    roomTypeId: request.RoomTypeId,
                    roomId: request.RoomId,
                    quotedRate: request.QuotedRate,
                    totalAmount: request.TotalAmount,
                    currency: request.Currency,
                    source: source,
                    specialRequests: request.SpecialRequests,
                    notes: request.Notes);

                foreach (var history in reservation.StatusHistories)
                    dbContext.ReservationStatusHistories.Add(history);

                dbContext.Reservations.Add(reservation);
                await dbContext.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var roomEntity = request.RoomId.HasValue
                    ? await dbContext.Rooms.FindAsync([request.RoomId.Value], ct) : null;
                var roomTypeEntity = request.RoomTypeId.HasValue
                    ? await dbContext.RoomTypes.FindAsync([request.RoomTypeId.Value], ct) : null;
                var histories = await dbContext.ReservationStatusHistories
                    .Where(h => h.ReservationId == reservation.Id)
                    .OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

                return Results.Created(
                    $"/api/v1/reservations/{reservation.Id}",
                    ToDto(reservation, guest, roomEntity, roomTypeEntity, histories));
            }
            catch (DbUpdateException ex) when (IsExclusionConstraintViolation(ex))
            {
                await tx.RollbackAsync(ct);
                return Results.Problem(
                    "Room is already booked for the requested dates (concurrent conflict detected).",
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<IResult> GetReservationByIdAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([id], ct);
        if (reservation == null || reservation.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
        var room = reservation.RoomId.HasValue
            ? await dbContext.Rooms.FindAsync([reservation.RoomId.Value], ct)
            : null;
        var roomType = reservation.RoomTypeId.HasValue
            ? await dbContext.RoomTypes.FindAsync([reservation.RoomTypeId.Value], ct)
            : null;

        var histories = await dbContext.ReservationStatusHistories
            .Where(h => h.ReservationId == id)
            .OrderBy(h => h.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(ToDto(reservation, guest, room, roomType, histories));
    }

    private static async Task<IResult> UpdateReservationAsync(
        Guid id,
        [FromBody] UpdateReservationRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([id], ct);
        if (reservation == null || reservation.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        if (!DateOnly.TryParse(request.CheckInDate, out var checkIn) ||
            !DateOnly.TryParse(request.CheckOutDate, out var checkOut))
            return Results.Problem("Invalid date format. Use YYYY-MM-DD.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            reservation.UpdateDetails(checkIn, checkOut, request.Adults, request.Children,
                request.QuotedRate, request.TotalAmount, request.RoomTypeId,
                request.SpecialRequests, request.Notes);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await dbContext.SaveChangesAsync(ct);

        var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
        var room = reservation.RoomId.HasValue ? await dbContext.Rooms.FindAsync([reservation.RoomId.Value], ct) : null;
        var roomType = reservation.RoomTypeId.HasValue ? await dbContext.RoomTypes.FindAsync([reservation.RoomTypeId.Value], ct) : null;
        var histories = await dbContext.ReservationStatusHistories
            .Where(h => h.ReservationId == id).OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

        return Results.Ok(ToDto(reservation, guest, room, roomType, histories));
    }

    private static async Task<IResult> ConfirmReservationAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IChannelAvailabilitySync channelSync,
        CancellationToken ct)
        => await PerformTransitionAsync(id, dbContext, tenantContext, ct,
            (r, _) =>
            {
                r.Confirm();
                dbContext.ReservationStatusHistories.Add(r.StatusHistories.Last());
            },
            afterSave: r => channelSync.TriggerAsync(r.PropertyId, r.RoomTypeId, r.CheckInDate, r.CheckOutDate, ct));

    private static async Task<IResult> AssignRoomAsync(
        Guid id,
        [FromBody] AssignRoomRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([id], ct);
        if (reservation == null || reservation.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        var room = await dbContext.Rooms.FindAsync([request.RoomId], ct);
        if (room == null || room.PropertyId != reservation.PropertyId)
            return Results.Problem("Room not found in this property.", statusCode: StatusCodes.Status400BadRequest);
        if (!room.IsActive)
            return Results.Problem("Room is not active.", statusCode: StatusCodes.Status400BadRequest);
        if (room.OperationalStatus is RoomOperationalStatus.OutOfService or RoomOperationalStatus.Maintenance)
            return Results.Problem($"Room is currently {room.OperationalStatus}.", statusCode: StatusCodes.Status409Conflict);

        // Wrap conflict check + assignment in a serializable transaction to close TOCTOU race.
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var hasConflict = await dbContext.Reservations.AnyAsync(r =>
                    r.Id != id &&
                    r.RoomId == request.RoomId &&
                    r.PropertyId == reservation.PropertyId &&
                    r.Status != ReservationStatus.Cancelled &&
                    r.Status != ReservationStatus.NoShow &&
                    r.CheckInDate < reservation.CheckOutDate &&
                    reservation.CheckInDate < r.CheckOutDate, ct);

                if (hasConflict)
                    return Results.Problem(
                        "Room is not available for the reservation dates.",
                        statusCode: StatusCodes.Status409Conflict);

                reservation.AssignRoom(request.RoomId);
                dbContext.ReservationStatusHistories.Add(reservation.StatusHistories.Last());
                await dbContext.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);

                var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
                var roomType = reservation.RoomTypeId.HasValue
                    ? await dbContext.RoomTypes.FindAsync([reservation.RoomTypeId.Value], ct) : null;
                var histories = await dbContext.ReservationStatusHistories
                    .Where(h => h.ReservationId == id).OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

                return Results.Ok(ToDto(reservation, guest, room, roomType, histories));
            }
            catch (DbUpdateException ex) when (IsExclusionConstraintViolation(ex))
            {
                await tx.RollbackAsync(ct);
                return Results.Problem(
                    "Room is already assigned to another reservation for these dates.",
                    statusCode: StatusCodes.Status409Conflict);
            }
            catch (InvalidOperationException ex)
            {
                await tx.RollbackAsync(ct);
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        });
    }

    private static async Task<IResult> CheckInAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
        => await PerformTransitionAsync(id, dbContext, tenantContext, ct,
            (r, db) =>
            {
                r.CheckIn();
                db.ReservationStatusHistories.Add(r.StatusHistories.Last());

                // On check-in, mark room dirty (if currently Clean/Inspected → Dirty is NOT set here;
                // it's a check-out event that sets to dirty. On check-in, room stays as-is.)
                // Room operational status is managed independently by housekeeping.
            });

    private static async Task<IResult> CheckOutAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IChannelAvailabilitySync channelSync,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([id], ct);
        if (reservation == null || reservation.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        try
        {
            reservation.CheckOut();
            dbContext.ReservationStatusHistories.Add(reservation.StatusHistories.Last());

            // After checkout, set room to Dirty
            if (reservation.RoomId.HasValue)
            {
                var room = await dbContext.Rooms.FindAsync([reservation.RoomId.Value], ct);
                if (room != null)
                {
                    var history = room.UpdateOperationalStatus(
                        RoomOperationalStatus.Dirty,
                        "Auto-set to Dirty after guest checkout",
                        tenantContext.UserId,
                        tenantContext.UserEmail);
                    dbContext.RoomStatusHistories.Add(history);
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await dbContext.SaveChangesAsync(ct);

        // Push freed availability back to channel manager (checkout frees up the room).
        try { await channelSync.TriggerAsync(reservation.PropertyId, reservation.RoomTypeId, reservation.CheckInDate, reservation.CheckOutDate, ct); }
        catch { /* logged inside sync */ }

        var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
        var room2 = reservation.RoomId.HasValue ? await dbContext.Rooms.FindAsync([reservation.RoomId.Value], ct) : null;
        var roomType = reservation.RoomTypeId.HasValue ? await dbContext.RoomTypes.FindAsync([reservation.RoomTypeId.Value], ct) : null;
        var histories = await dbContext.ReservationStatusHistories
            .Where(h => h.ReservationId == id).OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

        return Results.Ok(ToDto(reservation, guest, room2, roomType, histories));
    }

    private static async Task<IResult> CancelReservationAsync(
        Guid id,
        [FromBody] CancelReservationRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IChannelAvailabilitySync channelSync,
        CancellationToken ct)
        => await PerformTransitionAsync(id, dbContext, tenantContext, ct,
            (r, db) =>
            {
                r.Cancel(request.Reason);
                db.ReservationStatusHistories.Add(r.StatusHistories.Last());
            },
            afterSave: r => channelSync.TriggerAsync(r.PropertyId, r.RoomTypeId, r.CheckInDate, r.CheckOutDate, ct));

    private static async Task<IResult> MarkNoShowAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
        => await PerformTransitionAsync(id, dbContext, tenantContext, ct,
            (r, db) =>
            {
                r.MarkNoShow();
                db.ReservationStatusHistories.Add(r.StatusHistories.Last());
            });

    private static async Task<IResult> GetAvailabilityAsync(
        Guid propertyId,
        [FromQuery] string checkInDate,
        [FromQuery] string checkOutDate,
        [FromQuery] Guid? roomTypeId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        if (!DateOnly.TryParse(checkInDate, out var checkIn))
            return Results.Problem("Invalid check-in date format.", statusCode: StatusCodes.Status400BadRequest);

        if (!DateOnly.TryParse(checkOutDate, out var checkOut))
            return Results.Problem("Invalid check-out date format.", statusCode: StatusCodes.Status400BadRequest);

        if (checkOut <= checkIn)
            return Results.Problem("Check-out must be after check-in.", statusCode: StatusCodes.Status400BadRequest);

        // Validate property belongs to org
        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        if (property == null || property.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.Problem("Property not found.", statusCode: StatusCodes.Status400BadRequest);

        // Get rooms that are blocked (have active reservation in this period)
        var blockedRoomIds = await dbContext.Reservations
            .Where(r =>
                r.PropertyId == propertyId &&
                r.RoomId != null &&
                r.Status != ReservationStatus.Cancelled &&
                r.Status != ReservationStatus.NoShow &&
                r.CheckInDate < checkOut &&
                checkIn < r.CheckOutDate)
            .Select(r => r.RoomId!.Value)
            .Distinct()
            .ToListAsync(ct);

        // Get all active rooms for this property
        var roomsQuery = dbContext.Rooms
            .Where(r =>
                r.PropertyId == propertyId &&
                r.IsActive &&
                r.OperationalStatus != RoomOperationalStatus.OutOfService &&
                r.OperationalStatus != RoomOperationalStatus.Maintenance &&
                !blockedRoomIds.Contains(r.Id));

        if (roomTypeId.HasValue)
            roomsQuery = roomsQuery.Where(r => r.RoomTypeId == roomTypeId.Value);

        var availableRooms = await roomsQuery.ToListAsync(ct);

        // Get all relevant room types
        var roomTypeIds = availableRooms.Select(r => r.RoomTypeId).Distinct().ToList();
        var roomTypesQuery = dbContext.RoomTypes
            .Where(rt => rt.PropertyId == propertyId && rt.IsActive && roomTypeIds.Contains(rt.Id));

        var roomTypesList = await roomTypesQuery.OrderBy(rt => rt.Name).ToListAsync(ct);

        // Group rooms by room type
        var result = roomTypesList.Select(rt =>
        {
            var rooms = availableRooms
                .Where(r => r.RoomTypeId == rt.Id)
                .Select(r => new AvailableRoomDto(
                    r.Id,
                    r.RoomNumber,
                    r.DisplayName,
                    r.OperationalStatus.ToString()))
                .ToList();

            return new AvailabilityRoomTypeDto(
                rt.Id,
                rt.Name,
                rt.Code,
                rt.BaseRate,
                rt.StandardOccupancy,
                rt.MaxOccupancy,
                rooms.Count,
                rooms);
        }).ToList();

        return Results.Ok(result);
    }

    // ── Helper ────────────────────────────────────────────────────────────

    private static async Task<IResult> PerformTransitionAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct,
        Action<Reservation, StayFlowDbContext> transition,
        Func<Reservation, Task>? afterSave = null)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([id], ct);
        if (reservation == null || reservation.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        try
        {
            transition(reservation, dbContext);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        await dbContext.SaveChangesAsync(ct);

        // Post-save side effects (e.g. channel manager availability sync).
        // Exceptions are swallowed so PMS operations never fail due to channel manager issues.
        if (afterSave is not null)
        {
            try { await afterSave(reservation); }
            catch { /* logged inside the side-effect implementation */ }
        }

        var guest = await dbContext.Guests.FindAsync([reservation.GuestId], ct);
        var room = reservation.RoomId.HasValue ? await dbContext.Rooms.FindAsync([reservation.RoomId.Value], ct) : null;
        var roomType = reservation.RoomTypeId.HasValue ? await dbContext.RoomTypes.FindAsync([reservation.RoomTypeId.Value], ct) : null;
        var histories = await dbContext.ReservationStatusHistories
            .Where(h => h.ReservationId == id).OrderBy(h => h.CreatedAtUtc).ToListAsync(ct);

        return Results.Ok(ToDto(reservation, guest, room, roomType, histories));
    }

    /// <summary>
    /// Returns true when a DbUpdateException was caused by the PostgreSQL exclusion constraint
    /// "no_room_double_booking" (SqlState 23P01 — exclusion_violation).
    /// Falls back gracefully to false for non-PostgreSQL providers (e.g. InMemory in tests).
    /// </summary>
    private static bool IsExclusionConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        if (inner is null) return false;
        // Check via property reflection to avoid a hard compile-time dependency on Npgsql in this file.
        var sqlState = inner.GetType().GetProperty("SqlState")?.GetValue(inner) as string;
        return sqlState == "23P01";
    }

    private static ReservationDto ToDto(
        Reservation r,
        BuildingBlocks.Domain.Parties.Guest? guest,
        BuildingBlocks.Domain.Inventory.Room? room,
        BuildingBlocks.Domain.Inventory.RoomType? roomType,
        IEnumerable<ReservationStatusHistory> histories)
        => new(
            r.Id,
            r.OrganizationId,
            r.PropertyId,
            r.GuestId,
            guest?.FullName ?? "Unknown",
            guest?.Email,
            guest?.Phone,
            r.RoomTypeId,
            roomType?.Name,
            r.RoomId,
            room?.RoomNumber,
            r.CheckInDate.ToString("yyyy-MM-dd"),
            r.CheckOutDate.ToString("yyyy-MM-dd"),
            r.NumberOfNights,
            r.Adults,
            r.Children,
            r.QuotedRate,
            r.TotalAmount,
            r.Currency,
            r.Status.ToString(),
            r.Source.ToString(),
            r.SpecialRequests,
            r.Notes,
            r.CancellationReason,
            r.CancelledAtUtc,
            r.CheckedInAtUtc,
            r.CheckedOutAtUtc,
            r.CreatedAtUtc,
            r.UpdatedAtUtc,
            histories.Select(h => new ReservationStatusHistoryDto(
                h.Id,
                h.FromStatus?.ToString(),
                h.ToStatus.ToString(),
                h.Note,
                h.CreatedAtUtc)).ToList());
}

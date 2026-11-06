using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.ChannelManager;
using StayFlow.BuildingBlocks.Domain.ChannelManager;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Domain.Parties;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── Request / Response DTOs ──────────────────────────────────────────────────

public record CreateChannelConnectionRequest(
    int Provider,
    string ProviderPropertyId,
    string ApiKey,
    string? ApiSecret);

public record UpdateChannelConnectionRequest(
    string ProviderPropertyId,
    string ApiKey,
    string? ApiSecret);

public record ChannelConnectionDto(
    Guid Id,
    Guid PropertyId,
    int Provider,
    string ProviderName,
    string ProviderPropertyId,
    bool IsActive,
    DateTimeOffset? LastSyncAtUtc,
    string? LastSyncStatus,
    DateTimeOffset CreatedAtUtc);

public record CreateRateMappingRequest(
    Guid RoomTypeId,
    string ChannelRoomTypeCode,
    string ChannelRatePlanCode);

public record ChannelRateMappingDto(
    Guid Id,
    Guid ChannelConnectionId,
    Guid RoomTypeId,
    string ChannelRoomTypeCode,
    string ChannelRatePlanCode,
    bool IsActive);

public record ChannelReservationDto(
    Guid Id,
    string ChannelBookingReference,
    string OtaSource,
    Guid? ReservationId,
    string ProcessingStatus,
    DateTimeOffset ReceivedAtUtc,
    DateTimeOffset? ProcessedAtUtc,
    string? FailureReason);

// ── Endpoints ────────────────────────────────────────────────────────────────

public static class ChannelManagerEndpoints
{
    public static void MapChannelManagerEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api/v1")
            .WithTags("Channel Manager")
            .RequireAuthorization();

        // Channel Connections
        api.MapGet("/properties/{propertyId:guid}/channel-connections", ListConnectionsAsync);
        api.MapPost("/properties/{propertyId:guid}/channel-connections", CreateConnectionAsync);
        api.MapPut("/properties/{propertyId:guid}/channel-connections/{id:guid}", UpdateConnectionAsync);
        api.MapDelete("/properties/{propertyId:guid}/channel-connections/{id:guid}", DeleteConnectionAsync);

        // Rate Mappings
        api.MapGet("/properties/{propertyId:guid}/channel-connections/{connectionId:guid}/rate-mappings", ListRateMappingsAsync);
        api.MapPost("/properties/{propertyId:guid}/channel-connections/{connectionId:guid}/rate-mappings", CreateRateMappingAsync);
        api.MapDelete("/properties/{propertyId:guid}/channel-connections/{connectionId:guid}/rate-mappings/{mappingId:guid}", DeleteRateMappingAsync);

        // Channel Reservations (inbound booking log)
        api.MapGet("/properties/{propertyId:guid}/channel-reservations", ListChannelReservationsAsync);

        // Manual availability sync trigger
        api.MapPost("/properties/{propertyId:guid}/channel-connections/{id:guid}/sync", TriggerSyncAsync);

        // Webhook — no auth middleware; authenticated by HMAC signature
        app.MapPost("/api/v1/webhooks/channel/{connectionId:guid}", HandleWebhookAsync)
            .WithTags("Channel Manager Webhooks");
    }

    // ── Channel Connections ──────────────────────────────────────────────────

    private static async Task<IResult> ListConnectionsAsync(
        Guid propertyId,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var list = await db.ChannelConnections
            .Where(c => c.PropertyId == propertyId && c.IsActive)
            .OrderBy(c => c.Provider)
            .Select(c => ToDto(c))
            .ToListAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateConnectionAsync(
        Guid propertyId,
        [FromBody] CreateChannelConnectionRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!Enum.IsDefined(typeof(ChannelProvider), req.Provider))
            return Results.Problem("Invalid channel provider.", statusCode: 400);

        var property = await db.Properties.FirstOrDefaultAsync(p => p.Id == propertyId);
        if (property is null) return Results.NotFound();

        var provider = (ChannelProvider)req.Provider;
        var exists = await db.ChannelConnections
            .AnyAsync(c => c.PropertyId == propertyId && c.Provider == provider);
        if (exists)
            return Results.Problem($"A {provider} connection already exists for this property.", statusCode: 409);

        // In production, encrypt the API key before storing.
        // For now, store as-is; add encryption when a key management service is configured.
        var connection = ChannelConnection.Create(
            tenant.OrganizationId!.Value,
            propertyId,
            provider,
            req.ProviderPropertyId,
            req.ApiKey,
            req.ApiSecret);

        db.ChannelConnections.Add(connection);
        await db.SaveChangesAsync();
        return Results.Created($"/api/v1/properties/{propertyId}/channel-connections/{connection.Id}", ToDto(connection));
    }

    private static async Task<IResult> UpdateConnectionAsync(
        Guid propertyId,
        Guid id,
        [FromBody] UpdateChannelConnectionRequest req,
        StayFlowDbContext db)
    {
        var connection = await db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.PropertyId == propertyId);
        if (connection is null) return Results.NotFound();

        connection.Update(req.ProviderPropertyId, req.ApiKey, req.ApiSecret);
        await db.SaveChangesAsync();
        return Results.Ok(ToDto(connection));
    }

    private static async Task<IResult> DeleteConnectionAsync(
        Guid propertyId,
        Guid id,
        StayFlowDbContext db)
    {
        var connection = await db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.PropertyId == propertyId);
        if (connection is null) return Results.NotFound();

        connection.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ── Rate Mappings ────────────────────────────────────────────────────────

    private static async Task<IResult> ListRateMappingsAsync(
        Guid propertyId,
        Guid connectionId,
        StayFlowDbContext db)
    {
        var list = await db.ChannelRateMappings
            .Where(m => m.PropertyId == propertyId && m.ChannelConnectionId == connectionId && m.IsActive)
            .Select(m => new ChannelRateMappingDto(
                m.Id, m.ChannelConnectionId, m.RoomTypeId,
                m.ChannelRoomTypeCode, m.ChannelRatePlanCode, m.IsActive))
            .ToListAsync();
        return Results.Ok(list);
    }

    private static async Task<IResult> CreateRateMappingAsync(
        Guid propertyId,
        Guid connectionId,
        [FromBody] CreateRateMappingRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        var connection = await db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.PropertyId == propertyId);
        if (connection is null) return Results.NotFound("Channel connection not found.");

        var roomType = await db.RoomTypes
            .FirstOrDefaultAsync(rt => rt.Id == req.RoomTypeId && rt.PropertyId == propertyId);
        if (roomType is null) return Results.NotFound("Room type not found.");

        var exists = await db.ChannelRateMappings
            .AnyAsync(m => m.ChannelConnectionId == connectionId && m.RoomTypeId == req.RoomTypeId && m.IsActive);
        if (exists)
            return Results.Problem("A mapping for this room type already exists.", statusCode: 409);

        var mapping = ChannelRateMapping.Create(
            tenant.OrganizationId!.Value, propertyId, connectionId,
            req.RoomTypeId, req.ChannelRoomTypeCode, req.ChannelRatePlanCode);

        db.ChannelRateMappings.Add(mapping);
        await db.SaveChangesAsync();
        return Results.Created(
            $"/api/v1/properties/{propertyId}/channel-connections/{connectionId}/rate-mappings/{mapping.Id}",
            new ChannelRateMappingDto(mapping.Id, mapping.ChannelConnectionId, mapping.RoomTypeId,
                mapping.ChannelRoomTypeCode, mapping.ChannelRatePlanCode, mapping.IsActive));
    }

    private static async Task<IResult> DeleteRateMappingAsync(
        Guid propertyId,
        Guid connectionId,
        Guid mappingId,
        StayFlowDbContext db)
    {
        var mapping = await db.ChannelRateMappings
            .FirstOrDefaultAsync(m => m.Id == mappingId && m.ChannelConnectionId == connectionId);
        if (mapping is null) return Results.NotFound();

        mapping.Deactivate();
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    // ── Channel Reservations log ─────────────────────────────────────────────

    private static async Task<IResult> ListChannelReservationsAsync(
        Guid propertyId,
        [FromQuery] int? status,
        [FromQuery] int pageSize = 50,
        [FromQuery] int page = 1,
        StayFlowDbContext db = default!,
        ITenantContext tenant = default!)
    {
        // Verify the property belongs to the authenticated organization (tenant-scoped check)
        var propertyExists = await db.Properties.AnyAsync(p => p.Id == propertyId);
        if (!propertyExists) return Results.NotFound();

        pageSize = Math.Clamp(pageSize == 0 ? 50 : pageSize, 1, 200);
        page = Math.Max(page == 0 ? 1 : page, 1);

        var query = db.ChannelReservations
            .Where(r => r.PropertyId == propertyId);

        if (status.HasValue && Enum.IsDefined(typeof(ChannelBookingStatus), status.Value))
            query = query.Where(r => r.ProcessingStatus == (ChannelBookingStatus)status.Value);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new ChannelReservationDto(
                r.Id, r.ChannelBookingReference, r.OtaSource.ToString(),
                r.ReservationId, r.ProcessingStatus.ToString(),
                r.ReceivedAtUtc, r.ProcessedAtUtc, r.FailureReason))
            .ToListAsync();

        return Results.Ok(new { data = items, total, page, pageSize });
    }

    // ── Manual availability sync ─────────────────────────────────────────────

    private record TriggerSyncRequest(string FromDate, string ToDate);

    private static async Task<IResult> TriggerSyncAsync(
        Guid propertyId,
        Guid id,
        [FromBody] TriggerSyncRequest req,
        StayFlowDbContext db,
        IChannelAvailabilitySync channelSync,
        CancellationToken ct)
    {
        if (!DateOnly.TryParse(req.FromDate, out var fromDate) ||
            !DateOnly.TryParse(req.ToDate, out var toDate))
            return Results.Problem("Invalid date format. Use yyyy-MM-dd.", statusCode: 400);

        if (toDate <= fromDate)
            return Results.Problem("toDate must be after fromDate.", statusCode: 400);

        var connection = await db.ChannelConnections
            .FirstOrDefaultAsync(c => c.Id == id && c.PropertyId == propertyId && c.IsActive, ct);
        if (connection is null) return Results.NotFound();

        // Sync is fire-and-forget for the caller; failures are logged internally.
        await channelSync.TriggerAsync(propertyId, roomTypeId: null, fromDate, toDate, ct);

        return Results.Ok(new
        {
            message = "Availability sync triggered.",
            connectionId = id,
            fromDate = fromDate.ToString("yyyy-MM-dd"),
            toDate = toDate.ToString("yyyy-MM-dd")
        });
    }

    // ── Inbound Webhook ──────────────────────────────────────────────────────

    private static async Task<IResult> HandleWebhookAsync(
        Guid connectionId,
        HttpContext httpContext,
        StayFlowDbContext db,
        IEnumerable<IChannelManagerProvider> providers)
    {
        // Read raw payload for HMAC validation and audit storage
        httpContext.Request.EnableBuffering();
        using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
        var rawPayload = await reader.ReadToEndAsync();
        httpContext.Request.Body.Position = 0;

        var connection = await db.ChannelConnections
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == connectionId && c.IsActive);

        if (connection is null)
            return Results.NotFound("Channel connection not found or inactive.");

        var provider = providers.FirstOrDefault(p => p.Provider == connection.Provider);
        if (provider is null)
            return Results.Problem("Provider not supported.", statusCode: 501);

        // Validate HMAC signature
        var signatureHeader = httpContext.Request.Headers["X-Channel-Signature"].FirstOrDefault();
        // TODO: load webhook secret from encrypted config; use connection.EncryptedApiSecret as placeholder
        var webhookSecret = connection.EncryptedApiSecret ?? connection.EncryptedApiKey;
        if (!provider.ValidateWebhookSignature(rawPayload, signatureHeader, webhookSecret))
            return Results.Problem("Invalid webhook signature.", statusCode: 401);

        // Idempotency: parse booking reference before creating ChannelReservation
        var booking = await provider.ParseInboundBookingAsync(rawPayload);
        if (booking is null)
            return Results.Problem("Could not parse booking payload.", statusCode: 422);

        var alreadyExists = await db.ChannelReservations
            .IgnoreQueryFilters()
            .AnyAsync(r => r.ChannelConnectionId == connectionId
                        && r.ChannelBookingReference == booking.ChannelBookingReference);

        if (alreadyExists)
            return Results.Ok(new { message = "Duplicate booking — already processed." });

        var channelRes = ChannelReservation.CreatePending(
            connection.OrganizationId,
            connection.PropertyId,
            connectionId,
            booking.ChannelBookingReference,
            booking.OtaSource,
            rawPayload);

        db.ChannelReservations.Add(channelRes);
        await db.SaveChangesAsync();

        // Find or create guest
        var guest = await db.Guests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(g =>
                g.OrganizationId == connection.OrganizationId &&
                g.Email == booking.GuestEmail);

        if (guest is null)
        {
            guest = Guest.Create(connection.OrganizationId, booking.GuestFullName,
                booking.GuestEmail, booking.GuestPhone);
            db.Guests.Add(guest);
            await db.SaveChangesAsync();
        }

        // Find room type from rate mapping
        var mapping = await db.ChannelRateMappings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m =>
                m.ChannelConnectionId == connectionId &&
                m.ChannelRoomTypeCode == booking.ChannelRoomTypeCode &&
                m.IsActive);

        // Check availability conflict — guard mapping null before LINQ to avoid NullReferenceException
        bool conflict = false;
        if (mapping is not null)
        {
            conflict = await db.Reservations
                .IgnoreQueryFilters()
                .AnyAsync(r =>
                    r.PropertyId == connection.PropertyId &&
                    r.RoomTypeId == mapping.RoomTypeId &&
                    r.Status != ReservationStatus.Cancelled &&
                    r.Status != ReservationStatus.NoShow &&
                    r.CheckInDate < booking.CheckOutDate &&
                    r.CheckOutDate > booking.CheckInDate);
        }

        if (conflict || mapping is null)
        {
            channelRes.MarkFailed(mapping is null
                ? $"No rate mapping for channel room type '{booking.ChannelRoomTypeCode}'"
                : "No available rooms for the requested dates.");
            await db.SaveChangesAsync();
            return Results.Conflict(new { message = channelRes.FailureReason });
        }

        // Create PMS reservation
        var reservation = Reservation.CreateFromChannel(
            connection.OrganizationId,
            connection.PropertyId,
            guest.Id,
            mapping.RoomTypeId,
            booking.CheckInDate,
            booking.CheckOutDate,
            booking.Adults,
            booking.Children,
            booking.TotalAmount,
            booking.Currency,
            booking.OtaSource,
            booking.SpecialRequests);

        db.Reservations.Add(reservation);
        channelRes.MarkCreated(reservation.Id);
        connection.RecordSync(true);
        await db.SaveChangesAsync();

        return Results.Ok(new { reservationId = reservation.Id, channelReservationId = channelRes.Id });
    }

    private static ChannelConnectionDto ToDto(ChannelConnection c) =>
        new(c.Id, c.PropertyId, (int)c.Provider, c.Provider.ToString(),
            c.ProviderPropertyId, c.IsActive, c.LastSyncAtUtc, c.LastSyncStatus, c.CreatedAtUtc);
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StayFlow.BuildingBlocks.Domain.ChannelManager;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.ChannelManager;

public class ChannelAvailabilitySync : IChannelAvailabilitySync
{
    private readonly StayFlowDbContext _db;
    private readonly IEnumerable<IChannelManagerProvider> _providers;
    private readonly ILogger<ChannelAvailabilitySync> _logger;

    public ChannelAvailabilitySync(
        StayFlowDbContext db,
        IEnumerable<IChannelManagerProvider> providers,
        ILogger<ChannelAvailabilitySync> logger)
    {
        _db = db;
        _providers = providers;
        _logger = logger;
    }

    public async Task TriggerAsync(
        Guid propertyId,
        Guid? roomTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default)
    {
        try
        {
            await PushAvailabilityAsync(propertyId, roomTypeId, fromDate, toDate, ct);
        }
        catch (Exception ex)
        {
            // Channel manager failures must never propagate to PMS callers.
            _logger.LogError(ex,
                "Unhandled error in channel availability sync for property {PropertyId}", propertyId);
        }
    }

    private async Task PushAvailabilityAsync(
        Guid propertyId,
        Guid? roomTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        // 1. Load active channel connections for this property.
        var connections = await _db.ChannelConnections
            .Where(c => c.PropertyId == propertyId && c.IsActive)
            .ToListAsync(ct);

        if (connections.Count == 0) return;

        // 2. Load rate mappings (filtered to the room type if provided).
        var mappingsQuery = _db.ChannelRateMappings
            .Where(m => m.PropertyId == propertyId && m.IsActive);
        if (roomTypeId.HasValue)
            mappingsQuery = mappingsQuery.Where(m => m.RoomTypeId == roomTypeId.Value);
        var mappings = await mappingsQuery.ToListAsync(ct);

        if (mappings.Count == 0) return;

        // 3. For each room type that has at least one mapping, compute availability per date.
        var roomTypeIds = mappings.Select(m => m.RoomTypeId).Distinct().ToList();

        // Total active rooms per room type (capacity).
        var capacityByType = await _db.Rooms
            .Where(r => r.PropertyId == propertyId && r.IsActive && roomTypeIds.Contains(r.RoomTypeId))
            .GroupBy(r => r.RoomTypeId)
            .Select(g => new { RoomTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoomTypeId, x => x.Count, ct);

        // Active reservations (Confirmed or CheckedIn) overlapping any date in [fromDate, toDate).
        // We fetch once for the whole date range, then compute per-date in memory.
        var occupiedReservations = await _db.Reservations
            .Where(r =>
                r.PropertyId == propertyId &&
                roomTypeIds.Contains(r.RoomTypeId!.Value) &&
                r.Status != ReservationStatus.Cancelled &&
                r.Status != ReservationStatus.NoShow &&
                r.Status != ReservationStatus.CheckedOut &&
                r.CheckInDate < toDate &&
                r.CheckOutDate > fromDate)
            .Select(r => new { r.RoomTypeId, r.CheckInDate, r.CheckOutDate })
            .ToListAsync(ct);

        // 4. Build AvailabilityUpdate list covering each date in the range.
        var updates = new List<AvailabilityUpdate>();
        var currentDate = fromDate;
        while (currentDate < toDate)
        {
            foreach (var rt in roomTypeIds)
            {
                var capacity = capacityByType.GetValueOrDefault(rt, 0);
                var occupied = occupiedReservations.Count(r =>
                    r.RoomTypeId == rt &&
                    r.CheckInDate <= currentDate &&
                    r.CheckOutDate > currentDate);
                var available = Math.Max(0, capacity - occupied);

                // Add one update per mapping for this room type.
                foreach (var mapping in mappings.Where(m => m.RoomTypeId == rt))
                {
                    updates.Add(new AvailabilityUpdate(
                        mapping.ChannelRoomTypeCode,
                        mapping.ChannelRatePlanCode,
                        currentDate,
                        available,
                        Rate: 0,       // Rate sync is out of scope for this iteration.
                        Currency: "THB"));
                }
            }
            currentDate = currentDate.AddDays(1);
        }

        if (updates.Count == 0) return;

        // 5. Push to each active connection using the matching provider.
        foreach (var connection in connections)
        {
            var provider = _providers.FirstOrDefault(p => p.Provider == connection.Provider);
            if (provider is null)
            {
                _logger.LogWarning(
                    "No provider registered for {Provider} (connection {ConnectionId})",
                    connection.Provider, connection.Id);
                continue;
            }

            // Filter updates to mappings belonging to this connection.
            var connectionMappings = mappings
                .Where(m => m.ChannelConnectionId == connection.Id)
                .ToList();
            var connectionUpdates = updates
                .Where(u => connectionMappings.Any(m =>
                    m.ChannelRoomTypeCode == u.ChannelRoomTypeCode &&
                    m.ChannelRatePlanCode == u.ChannelRatePlanCode))
                .ToList();

            if (connectionUpdates.Count == 0) continue;

            try
            {
                var success = await provider.PushAvailabilityAsync(connection, connectionUpdates, ct);
                connection.RecordSync(success, success ? "OK" : "Provider returned failure");
                _logger.LogInformation(
                    "Channel sync {Result} for property {PropertyId} via {Provider} ({UpdateCount} updates)",
                    success ? "succeeded" : "failed", propertyId, connection.Provider, connectionUpdates.Count);
            }
            catch (Exception ex)
            {
                connection.RecordSync(false, ex.Message.Length > 200
                    ? ex.Message[..200]
                    : ex.Message);
                _logger.LogError(ex,
                    "Channel sync failed for property {PropertyId} via {Provider}",
                    propertyId, connection.Provider);
            }
        }

        // Persist sync status updates on connections (best-effort; don't throw).
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist channel sync status for property {PropertyId}", propertyId);
        }
    }
}

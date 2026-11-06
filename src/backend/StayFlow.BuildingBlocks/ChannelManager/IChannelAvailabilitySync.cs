namespace StayFlow.BuildingBlocks.ChannelManager;

public interface IChannelAvailabilitySync
{
    /// <summary>
    /// Recalculates availability for <paramref name="roomTypeId"/> (or all mapped room types
    /// for the property if null) across [<paramref name="fromDate"/>, <paramref name="toDate"/>)
    /// and pushes updated counts to every active channel connection.
    ///
    /// Never throws — channel manager unavailability must not fail PMS operations.
    /// Sync failures are recorded on the ChannelConnection and logged.
    /// </summary>
    Task TriggerAsync(
        Guid propertyId,
        Guid? roomTypeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct = default);
}

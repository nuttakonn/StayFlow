using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.ChannelManager;

public class ChannelRateMapping : PropertyEntity<Guid>
{
    public Guid ChannelConnectionId { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public string ChannelRoomTypeCode { get; private set; } = string.Empty;
    public string ChannelRatePlanCode { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    private ChannelRateMapping() { }

    public static ChannelRateMapping Create(
        Guid organizationId,
        Guid propertyId,
        Guid channelConnectionId,
        Guid roomTypeId,
        string channelRoomTypeCode,
        string channelRatePlanCode)
    {
        if (string.IsNullOrWhiteSpace(channelRoomTypeCode))
            throw new ArgumentException("Channel room type code is required.", nameof(channelRoomTypeCode));
        if (string.IsNullOrWhiteSpace(channelRatePlanCode))
            throw new ArgumentException("Channel rate plan code is required.", nameof(channelRatePlanCode));

        return new ChannelRateMapping
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ChannelConnectionId = channelConnectionId,
            RoomTypeId = roomTypeId,
            ChannelRoomTypeCode = channelRoomTypeCode.Trim(),
            ChannelRatePlanCode = channelRatePlanCode.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public void Deactivate() { IsActive = false; Touch(); }
}

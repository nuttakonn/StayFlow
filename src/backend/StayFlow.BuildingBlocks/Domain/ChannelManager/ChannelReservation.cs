using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.ChannelManager;

public enum OtaSource
{
    Unknown = 0,
    Agoda = 1,
    BookingCom = 2,
    Traveloka = 3,
    TripCom = 4,
    Expedia = 5,
    Airbnb = 6,
    Other = 99
}

public enum ChannelBookingStatus
{
    Pending = 1,
    Created = 2,
    Duplicate = 3,
    Failed = 4,
    Cancelled = 5
}

public class ChannelReservation : PropertyEntity<Guid>
{
    public Guid ChannelConnectionId { get; private set; }
    public string ChannelBookingReference { get; private set; } = string.Empty;
    public OtaSource OtaSource { get; private set; }
    public Guid? ReservationId { get; private set; }
    public string RawPayload { get; private set; } = string.Empty;
    public ChannelBookingStatus ProcessingStatus { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    private ChannelReservation() { }

    public static ChannelReservation CreatePending(
        Guid organizationId,
        Guid propertyId,
        Guid channelConnectionId,
        string channelBookingReference,
        OtaSource otaSource,
        string rawPayload)
    {
        return new ChannelReservation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ChannelConnectionId = channelConnectionId,
            ChannelBookingReference = channelBookingReference.Trim(),
            OtaSource = otaSource,
            RawPayload = rawPayload,
            ProcessingStatus = ChannelBookingStatus.Pending,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public void MarkCreated(Guid reservationId)
    {
        ReservationId = reservationId;
        ProcessingStatus = ChannelBookingStatus.Created;
        ProcessedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkDuplicate()
    {
        ProcessingStatus = ChannelBookingStatus.Duplicate;
        ProcessedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        ProcessingStatus = ChannelBookingStatus.Failed;
        FailureReason = reason;
        ProcessedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkCancelled()
    {
        ProcessingStatus = ChannelBookingStatus.Cancelled;
        ProcessedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}

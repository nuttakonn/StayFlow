using StayFlow.BuildingBlocks.Domain.ChannelManager;

namespace StayFlow.BuildingBlocks.ChannelManager;

public record AvailabilityUpdate(
    string ChannelRoomTypeCode,
    string ChannelRatePlanCode,
    DateOnly Date,
    int AvailableCount,
    decimal Rate,
    string Currency);

public record InboundBookingDto(
    string ChannelBookingReference,
    OtaSource OtaSource,
    string GuestFullName,
    string? GuestEmail,
    string? GuestPhone,
    string ChannelRoomTypeCode,
    string ChannelRatePlanCode,
    DateOnly CheckInDate,
    DateOnly CheckOutDate,
    int Adults,
    int Children,
    decimal TotalAmount,
    string Currency,
    string? SpecialRequests);

public interface IChannelManagerProvider
{
    ChannelProvider Provider { get; }

    Task<bool> PushAvailabilityAsync(
        ChannelConnection connection,
        IReadOnlyList<AvailabilityUpdate> updates,
        CancellationToken ct = default);

    Task<InboundBookingDto?> ParseInboundBookingAsync(
        string rawPayload,
        CancellationToken ct = default);

    bool ValidateWebhookSignature(
        string rawPayload,
        string? signatureHeader,
        string webhookSecret);
}

using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.BookingEngine;

public enum HoldStatus { Active = 0, Released = 1, Expired = 2, Converted = 3 }

/// <summary>
/// Server-side timer lock that reserves a room type for a guest while they complete
/// the booking process. Automatically expires after HoldDurationMinutes.
/// </summary>
public class BookingHold : PropertyEntity<Guid>
{
    public Guid SessionToken { get; private set; }
    public Guid RoomTypeId { get; private set; }
    public DateOnly CheckInDate { get; private set; }
    public DateOnly CheckOutDate { get; private set; }
    public int Adults { get; private set; }
    public int Children { get; private set; }
    public HoldStatus Status { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public Guid? ConvertedReservationId { get; private set; }
    public string? GuestEmail { get; private set; }

    public bool IsActive => Status == HoldStatus.Active && ExpiresAtUtc > DateTimeOffset.UtcNow;

    private BookingHold() { }

    public static BookingHold Create(
        Guid orgId, Guid propId, Guid roomTypeId,
        DateOnly checkIn, DateOnly checkOut,
        int adults, int children,
        int holdMinutes, string? guestEmail)
    {
        return new BookingHold
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            PropertyId = propId,
            SessionToken = Guid.NewGuid(),
            RoomTypeId = roomTypeId,
            CheckInDate = checkIn,
            CheckOutDate = checkOut,
            Adults = adults,
            Children = children,
            Status = HoldStatus.Active,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(holdMinutes),
            GuestEmail = guestEmail?.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Release()
    {
        Status = HoldStatus.Released;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Expire()
    {
        Status = HoldStatus.Expired;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Convert(Guid reservationId)
    {
        Status = HoldStatus.Converted;
        ConvertedReservationId = reservationId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

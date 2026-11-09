using StayFlow.BuildingBlocks.Common;
using StayFlow.BuildingBlocks.Domain.ChannelManager;

namespace StayFlow.BuildingBlocks.Domain.Reservations;

public enum ReservationStatus
{
    Pending = 1,
    Confirmed = 2,
    CheckedIn = 3,
    CheckedOut = 4,
    Cancelled = 5,
    NoShow = 6
}

public enum ReservationSource
{
    WalkIn = 1,
    Phone = 2,
    Email = 3,
    Online = 4,
    OTA = 5,
    Other = 6,
    BookingEngine = 7
}

public class Reservation : PropertyEntity<Guid>
{
    public Guid GuestId { get; private set; }
    public Guid? RoomTypeId { get; private set; }
    public Guid? RoomId { get; private set; }

    // Half-open interval: [CheckInDate, CheckOutDate)
    public DateOnly CheckInDate { get; private set; }
    public DateOnly CheckOutDate { get; private set; }

    public int Adults { get; private set; } = 1;
    public int Children { get; private set; } = 0;

    public decimal QuotedRate { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "THB";

    public ReservationStatus Status { get; private set; } = ReservationStatus.Pending;
    public ReservationSource Source { get; private set; } = ReservationSource.Phone;

    // Rate plan and coupon locked at booking time — never recalculate silently
    public Guid? RatePlanId { get; private set; }
    public Guid? GroupBookingId { get; private set; }
    public string? CouponCode { get; private set; }
    public decimal DiscountAmount { get; private set; } = 0m;

    public string? SpecialRequests { get; private set; }
    public string? Notes { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DateTimeOffset? CheckedInAtUtc { get; private set; }
    public DateTimeOffset? CheckedOutAtUtc { get; private set; }

    // Concurrency token for optimistic concurrency
    public Guid RowVersion { get; private set; } = Guid.NewGuid();

    private readonly List<ReservationStatusHistory> _statusHistories = [];
    public IReadOnlyCollection<ReservationStatusHistory> StatusHistories => _statusHistories.AsReadOnly();

    private Reservation() { } // EF Core

    public static Reservation Create(
        Guid organizationId,
        Guid propertyId,
        Guid guestId,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int adults = 1,
        int children = 0,
        Guid? roomTypeId = null,
        Guid? roomId = null,
        decimal quotedRate = 0m,
        decimal totalAmount = 0m,
        string currency = "THB",
        ReservationSource source = ReservationSource.Phone,
        string? specialRequests = null,
        string? notes = null,
        Guid? ratePlanId = null,
        Guid? groupBookingId = null,
        string? couponCode = null,
        decimal discountAmount = 0m)
    {
        if (checkOutDate <= checkInDate)
            throw new ArgumentException("Check-out date must be after check-in date.");

        if (adults < 1)
            throw new ArgumentException("At least one adult is required.");

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            GuestId = guestId,
            RoomTypeId = roomTypeId,
            RoomId = roomId,
            CheckInDate = checkInDate,
            CheckOutDate = checkOutDate,
            Adults = adults,
            Children = Math.Max(0, children),
            QuotedRate = quotedRate,
            TotalAmount = totalAmount,
            Currency = currency,
            Status = ReservationStatus.Pending,
            Source = source,
            SpecialRequests = specialRequests?.Trim(),
            Notes = notes?.Trim(),
            RatePlanId = ratePlanId,
            GroupBookingId = groupBookingId,
            CouponCode = couponCode?.Trim().ToUpperInvariant(),
            DiscountAmount = discountAmount,
            RowVersion = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        reservation._statusHistories.Add(ReservationStatusHistory.Create(
            organizationId, propertyId, reservation.Id,
            null, ReservationStatus.Pending, "Reservation created"));

        return reservation;
    }

    /// <summary>
    /// Creates a Confirmed reservation from an inbound OTA booking.
    /// OTA bookings arrive pre-confirmed; a Pending→Confirmed history entry is added automatically.
    /// </summary>
    public static Reservation CreateFromChannel(
        Guid organizationId,
        Guid propertyId,
        Guid guestId,
        Guid roomTypeId,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int adults,
        int children,
        decimal totalAmount,
        string currency,
        OtaSource otaSource,
        string? specialRequests = null)
    {
        if (checkOutDate <= checkInDate)
            throw new ArgumentException("Check-out date must be after check-in date.");

        var reservation = new Reservation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            GuestId = guestId,
            RoomTypeId = roomTypeId,
            CheckInDate = checkInDate,
            CheckOutDate = checkOutDate,
            Adults = Math.Max(1, adults),
            Children = Math.Max(0, children),
            QuotedRate = totalAmount,
            TotalAmount = totalAmount,
            Currency = currency,
            Status = ReservationStatus.Confirmed,
            Source = ReservationSource.OTA,
            SpecialRequests = specialRequests?.Trim(),
            RowVersion = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        reservation._statusHistories.Add(ReservationStatusHistory.Create(
            organizationId, propertyId, reservation.Id,
            null, ReservationStatus.Pending, $"OTA booking received from {otaSource}"));

        reservation._statusHistories.Add(ReservationStatusHistory.Create(
            organizationId, propertyId, reservation.Id,
            ReservationStatus.Pending, ReservationStatus.Confirmed, "Auto-confirmed from channel manager"));

        return reservation;
    }

    public int NumberOfNights => CheckOutDate.DayNumber - CheckInDate.DayNumber;

    public void Confirm()
    {
        if (Status != ReservationStatus.Pending)
            throw new InvalidOperationException($"Cannot confirm a reservation in status '{Status}'. Only Pending reservations can be confirmed.");

        Status = ReservationStatus.Confirmed;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            ReservationStatus.Pending, ReservationStatus.Confirmed, "Reservation confirmed"));
    }

    public void AssignRoom(Guid roomId)
    {
        if (Status is not (ReservationStatus.Pending or ReservationStatus.Confirmed))
            throw new InvalidOperationException($"Cannot assign room to a reservation in status '{Status}'.");

        RoomId = roomId;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            Status, Status, $"Room assigned: {roomId}"));
    }

    public void MoveRoom(Guid newRoomId, string? oldRoomNumber, string? newRoomNumber, string reason)
    {
        if (Status != ReservationStatus.CheckedIn)
            throw new InvalidOperationException($"Cannot move room for a reservation in status '{Status}'. Only CheckedIn reservations can be moved.");

        if (RoomId == newRoomId)
            throw new InvalidOperationException("The target room is the same as the currently assigned room.");

        var oldRoom = oldRoomNumber ?? RoomId?.ToString() ?? "Unknown";
        var targetRoom = newRoomNumber ?? newRoomId.ToString();

        RoomId = newRoomId;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            ReservationStatus.CheckedIn, ReservationStatus.CheckedIn,
            $"Room moved from {oldRoom} to {targetRoom}. Reason: {reason}"));
    }

    public void CheckIn(DateTimeOffset? checkedInAt = null)
    {
        if (Status != ReservationStatus.Confirmed)
            throw new InvalidOperationException($"Cannot check in a reservation in status '{Status}'. Only Confirmed reservations can be checked in.");

        if (RoomId == null)
            throw new InvalidOperationException("A room must be assigned before checking in.");

        var previousStatus = Status;
        Status = ReservationStatus.CheckedIn;
        CheckedInAtUtc = checkedInAt ?? DateTimeOffset.UtcNow;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            previousStatus, ReservationStatus.CheckedIn, "Guest checked in"));
    }

    public void CheckOut(DateTimeOffset? checkedOutAt = null)
    {
        if (Status != ReservationStatus.CheckedIn)
            throw new InvalidOperationException($"Cannot check out a reservation in status '{Status}'. Only CheckedIn reservations can be checked out.");

        var previousStatus = Status;
        Status = ReservationStatus.CheckedOut;
        CheckedOutAtUtc = checkedOutAt ?? DateTimeOffset.UtcNow;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            previousStatus, ReservationStatus.CheckedOut, "Guest checked out"));
    }

    public void Cancel(string reason)
    {
        if (Status is ReservationStatus.CheckedIn or ReservationStatus.CheckedOut)
            throw new InvalidOperationException($"Cannot cancel a reservation in status '{Status}'.");

        if (Status == ReservationStatus.Cancelled)
            throw new InvalidOperationException("Reservation is already cancelled.");

        var previousStatus = Status;
        Status = ReservationStatus.Cancelled;
        CancellationReason = reason?.Trim();
        CancelledAtUtc = DateTimeOffset.UtcNow;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            previousStatus, ReservationStatus.Cancelled, $"Cancelled: {reason}"));
    }

    public void MarkNoShow()
    {
        if (Status != ReservationStatus.Confirmed)
            throw new InvalidOperationException($"Cannot mark no-show for a reservation in status '{Status}'. Only Confirmed reservations can be marked as No Show.");

        var previousStatus = Status;
        Status = ReservationStatus.NoShow;
        CancelledAtUtc = DateTimeOffset.UtcNow;
        RowVersion = Guid.NewGuid();
        Touch();

        _statusHistories.Add(ReservationStatusHistory.Create(
            OrganizationId, PropertyId, Id,
            previousStatus, ReservationStatus.NoShow, "Marked as no-show"));
    }

    public void UpdateDetails(
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int adults,
        int children,
        decimal quotedRate,
        decimal totalAmount,
        Guid? roomTypeId,
        string? specialRequests,
        string? notes)
    {
        if (Status is ReservationStatus.CheckedIn or ReservationStatus.CheckedOut or ReservationStatus.Cancelled or ReservationStatus.NoShow)
            throw new InvalidOperationException($"Cannot update a reservation in status '{Status}'.");

        if (checkOutDate <= checkInDate)
            throw new ArgumentException("Check-out date must be after check-in date.");

        CheckInDate = checkInDate;
        CheckOutDate = checkOutDate;
        Adults = Math.Max(1, adults);
        Children = Math.Max(0, children);
        QuotedRate = quotedRate;
        TotalAmount = totalAmount;
        RoomTypeId = roomTypeId;
        SpecialRequests = specialRequests?.Trim();
        Notes = notes?.Trim();
        RowVersion = Guid.NewGuid();
        Touch();
    }

    public void SetGroupBooking(Guid groupBookingId)
    {
        GroupBookingId = groupBookingId;
        RowVersion = Guid.NewGuid();
        Touch();
    }

    /// <summary>
    /// Checks if this reservation overlaps with a given date range [start, end).
    /// Half-open interval: existing.Start < requested.End AND requested.Start < existing.End
    /// </summary>
    public bool OverlapsWith(DateOnly startDate, DateOnly endDate)
        => CheckInDate < endDate && startDate < CheckOutDate;
}

public class ReservationStatusHistory : PropertyEntity<Guid>
{
    public Guid ReservationId { get; private set; }
    public ReservationStatus? FromStatus { get; private set; }
    public ReservationStatus ToStatus { get; private set; }
    public string? Note { get; private set; }

    private ReservationStatusHistory() { } // EF Core

    public static ReservationStatusHistory Create(
        Guid organizationId,
        Guid propertyId,
        Guid reservationId,
        ReservationStatus? fromStatus,
        ReservationStatus toStatus,
        string? note = null)
    {
        return new ReservationStatusHistory
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ReservationId = reservationId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Note = note?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

using StayFlow.BuildingBlocks.Domain.Reservations;

namespace StayFlow.UnitTests;

/// <summary>
/// Unit tests for Reservation.OverlapsWith and the domain-level double-booking invariant.
///
/// These tests verify the half-open interval logic [CheckInDate, CheckOutDate) used
/// throughout the availability engine and concurrency protection.
/// </summary>
public class ReservationConcurrencyTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid PropId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();
    private static readonly Guid RoomTypeId = Guid.NewGuid();

    private static Reservation MakeReservation(DateOnly checkIn, DateOnly checkOut)
        => Reservation.Create(OrgId, PropId, GuestId, checkIn, checkOut,
            roomTypeId: RoomTypeId);

    // ── OverlapsWith ─────────────────────────────────────────────────────────

    [Fact]
    public void OverlapsWith_IdenticalDates_ReturnsTrue()
    {
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.True(r.OverlapsWith(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13)));
    }

    [Fact]
    public void OverlapsWith_PartialOverlapAtStart_ReturnsTrue()
    {
        // Existing: Jun 10–13. New: Jun 8–11 overlaps.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.True(r.OverlapsWith(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 11)));
    }

    [Fact]
    public void OverlapsWith_PartialOverlapAtEnd_ReturnsTrue()
    {
        // Existing: Jun 10–13. New: Jun 12–15 overlaps.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.True(r.OverlapsWith(new DateOnly(2026, 6, 12), new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void OverlapsWith_NewContainsExisting_ReturnsTrue()
    {
        // Existing: Jun 10–13. New: Jun 8–15 fully contains existing.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.True(r.OverlapsWith(new DateOnly(2026, 6, 8), new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void OverlapsWith_ExistingContainsNew_ReturnsTrue()
    {
        // Existing: Jun 10–15. New: Jun 11–13 is fully inside.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 15));
        Assert.True(r.OverlapsWith(new DateOnly(2026, 6, 11), new DateOnly(2026, 6, 13)));
    }

    [Fact]
    public void OverlapsWith_AdjacentCheckout_ReturnsFalse()
    {
        // Existing: Jun 10–13. New: Jun 13–15. No overlap — half-open interval.
        // Jun 13 checkout means Jun 12 is the last occupied night.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.False(r.OverlapsWith(new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void OverlapsWith_AdjacentCheckin_ReturnsFalse()
    {
        // Existing: Jun 13–15. New: Jun 10–13. No overlap.
        var r = MakeReservation(new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 15));
        Assert.False(r.OverlapsWith(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13)));
    }

    [Fact]
    public void OverlapsWith_CompletelyBefore_ReturnsFalse()
    {
        // Existing: Jun 10–13. New: Jun 1–5. No overlap.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.False(r.OverlapsWith(new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 5)));
    }

    [Fact]
    public void OverlapsWith_CompletelyAfter_ReturnsFalse()
    {
        // Existing: Jun 10–13. New: Jun 20–25. No overlap.
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.False(r.OverlapsWith(new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 25)));
    }

    // ── Domain invariants ────────────────────────────────────────────────────

    [Fact]
    public void Create_WithCheckoutBeforeCheckin_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MakeReservation(new DateOnly(2026, 6, 13), new DateOnly(2026, 6, 10)));
        Assert.Contains("after", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_WithCheckoutEqualToCheckin_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 10)));
        Assert.Contains("after", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Confirm_ThenCancel_StatusIsCancelled()
    {
        var r = MakeReservation(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5));
        r.Confirm();
        r.Cancel("Guest requested cancellation");
        Assert.Equal(ReservationStatus.Cancelled, r.Status);
    }

    [Fact]
    public void Confirm_AlreadyCancelled_Throws()
    {
        var r = MakeReservation(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 5));
        r.Cancel("test");
        Assert.Throws<InvalidOperationException>(() => r.Confirm());
    }

    [Fact]
    public void NumberOfNights_IsCorrect()
    {
        var r = MakeReservation(new DateOnly(2026, 6, 10), new DateOnly(2026, 6, 13));
        Assert.Equal(3, r.NumberOfNights);
    }

    // ── CreateFromChannel ────────────────────────────────────────────────────

    [Fact]
    public void CreateFromChannel_SetsStatusConfirmed()
    {
        var r = Reservation.CreateFromChannel(
            OrgId, PropId, GuestId, RoomTypeId,
            new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 5),
            adults: 2, children: 0,
            totalAmount: 8000m, currency: "THB",
            otaSource: BuildingBlocks.Domain.ChannelManager.OtaSource.Agoda);

        Assert.Equal(ReservationStatus.Confirmed, r.Status);
        Assert.Equal(ReservationSource.OTA, r.Source);
        Assert.Equal(4, r.NumberOfNights);
    }
}

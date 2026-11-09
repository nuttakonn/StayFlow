using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Services;

public enum QuotationStatus
{
    Draft = 0,
    Sent = 1,
    Accepted = 2,
    Expired = 3,
    Converted = 4,
    Cancelled = 5
}

public class Quotation : PropertyEntity<Guid>
{
    public string QuotationNumber { get; private set; } = string.Empty;
    public Guid GuestId { get; private set; }
    public Guid? RoomTypeId { get; private set; }
    public DateOnly CheckInDate { get; private set; }
    public DateOnly CheckOutDate { get; private set; }
    public int Adults { get; private set; }
    public int Children { get; private set; }
    public Guid? RatePlanId { get; private set; }
    public decimal QuotedRatePerNight { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string Currency { get; private set; } = "THB";
    public QuotationStatus Status { get; private set; } = QuotationStatus.Draft;
    public DateOnly? ExpiryDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ConvertedReservationId { get; private set; }

    private Quotation() { }

    public static Quotation Create(
        Guid organizationId,
        Guid propertyId,
        string quotationNumber,
        Guid guestId,
        Guid? roomTypeId,
        DateOnly checkIn,
        DateOnly checkOut,
        int adults,
        int children,
        Guid? ratePlanId,
        decimal ratePerNight,
        decimal totalAmount,
        string currency,
        DateOnly? expiryDate,
        string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            QuotationNumber = quotationNumber.Trim().ToUpperInvariant(),
            GuestId = guestId,
            RoomTypeId = roomTypeId,
            CheckInDate = checkIn,
            CheckOutDate = checkOut,
            Adults = adults,
            Children = children,
            RatePlanId = ratePlanId,
            QuotedRatePerNight = ratePerNight,
            TotalAmount = totalAmount,
            Currency = currency,
            ExpiryDate = expiryDate,
            Notes = notes?.Trim()
        };

    public void MarkSent() { Status = QuotationStatus.Sent; UpdatedAtUtc = DateTimeOffset.UtcNow; }
    public void MarkAccepted() { Status = QuotationStatus.Accepted; UpdatedAtUtc = DateTimeOffset.UtcNow; }
    public void Cancel() { Status = QuotationStatus.Cancelled; UpdatedAtUtc = DateTimeOffset.UtcNow; }

    public void MarkConverted(Guid reservationId)
    {
        if (Status == QuotationStatus.Converted)
            throw new InvalidOperationException("Quotation already converted.");
        if (Status == QuotationStatus.Cancelled)
            throw new InvalidOperationException("Cannot convert a cancelled quotation.");

        Status = QuotationStatus.Converted;
        ConvertedReservationId = reservationId;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public int Nights => CheckOutDate.DayNumber - CheckInDate.DayNumber;
}

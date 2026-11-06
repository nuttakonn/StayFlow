using StayFlow.BuildingBlocks.Common;
using StayFlow.BuildingBlocks.Domain.Inventory;

namespace StayFlow.BuildingBlocks.Domain.Apartments;

public enum UtilityType
{
    Electricity = 1,
    Water = 2
}

public class MeterReading : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public Guid RoomId { get; private set; }
    public Room? Room { get; private set; }

    public UtilityType UtilityType { get; private set; }
    public DateOnly ReadingDate { get; private set; }
    public string BillingMonth { get; private set; } = string.Empty; // e.g. "2026-11"
    public decimal PreviousReading { get; private set; }
    public decimal CurrentReading { get; private set; }
    public decimal UnitPrice { get; private set; }

    public decimal UnitsUsed => Math.Max(0, CurrentReading - PreviousReading);
    public decimal TotalAmount => Math.Round(UnitsUsed * UnitPrice, 2);

    public Guid? RecordedByUserId { get; private set; }
    public string? Notes { get; private set; }

    private MeterReading() { } // EF Core

    public static MeterReading Record(
        Guid organizationId,
        Guid propertyId,
        Guid roomId,
        UtilityType utilityType,
        DateOnly readingDate,
        string billingMonth,
        decimal previousReading,
        decimal currentReading,
        decimal unitPrice,
        Guid? recordedByUserId = null,
        string? notes = null)
    {
        if (currentReading < previousReading)
            throw new InvalidOperationException($"Current reading ({currentReading}) cannot be less than previous reading ({previousReading}).");

        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");

        ArgumentException.ThrowIfNullOrWhiteSpace(billingMonth);

        return new MeterReading
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            RoomId = roomId,
            UtilityType = utilityType,
            ReadingDate = readingDate,
            BillingMonth = billingMonth.Trim(),
            PreviousReading = previousReading,
            CurrentReading = currentReading,
            UnitPrice = unitPrice,
            RecordedByUserId = recordedByUserId,
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

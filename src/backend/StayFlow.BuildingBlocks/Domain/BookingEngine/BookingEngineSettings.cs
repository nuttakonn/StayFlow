using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.BookingEngine;

/// <summary>Per-property configuration for the direct booking engine.</summary>
public class BookingEngineSettings : PropertyEntity<Guid>
{
    public bool IsEnabled { get; private set; } = false;
    public int HoldDurationMinutes { get; private set; } = 15;
    public string BookingPolicyText { get; private set; } = string.Empty;
    public int MinAdvanceBookingDays { get; private set; } = 0;
    public int MaxAdvanceBookingDays { get; private set; } = 365;

    private BookingEngineSettings() { }

    public static BookingEngineSettings CreateDefault(Guid orgId, Guid propId) => new()
    {
        Id = Guid.NewGuid(),
        OrganizationId = orgId,
        PropertyId = propId,
        IsEnabled = false,
        HoldDurationMinutes = 15,
        BookingPolicyText = string.Empty,
        MinAdvanceBookingDays = 0,
        MaxAdvanceBookingDays = 365,
        CreatedAtUtc = DateTimeOffset.UtcNow
    };

    public void Update(
        bool isEnabled, int holdDurationMinutes, string bookingPolicyText,
        int minAdvanceBookingDays, int maxAdvanceBookingDays)
    {
        IsEnabled = isEnabled;
        HoldDurationMinutes = holdDurationMinutes;
        BookingPolicyText = bookingPolicyText;
        MinAdvanceBookingDays = minAdvanceBookingDays;
        MaxAdvanceBookingDays = maxAdvanceBookingDays;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

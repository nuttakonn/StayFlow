using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Rates;

public enum GroupBookingStatus { Active = 0, Cancelled = 1 }

public class GroupBooking : PropertyEntity<Guid>
{
    public string CoordinatorName { get; private set; } = string.Empty;
    public string? CoordinatorEmail { get; private set; }
    public string? CoordinatorPhone { get; private set; }
    public string? CompanyName { get; private set; }
    public string? AgreedRateNotes { get; private set; }
    public string? Notes { get; private set; }
    public GroupBookingStatus Status { get; private set; } = GroupBookingStatus.Active;
    public string? CancellationReason { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    private GroupBooking() { }

    public static GroupBooking Create(
        Guid organizationId,
        Guid propertyId,
        string coordinatorName,
        string? coordinatorEmail,
        string? coordinatorPhone,
        string? companyName,
        string? agreedRateNotes,
        string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            CoordinatorName = coordinatorName.Trim(),
            CoordinatorEmail = coordinatorEmail?.Trim(),
            CoordinatorPhone = coordinatorPhone?.Trim(),
            CompanyName = companyName?.Trim(),
            AgreedRateNotes = agreedRateNotes?.Trim(),
            Notes = notes?.Trim()
        };

    public void Cancel(string reason)
    {
        if (Status == GroupBookingStatus.Cancelled) return;
        Status = GroupBookingStatus.Cancelled;
        CancellationReason = reason;
        CancelledAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

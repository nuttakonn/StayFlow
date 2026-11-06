using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Maintenance;

public enum MaintenancePriority { Low = 1, Medium = 2, High = 3, Urgent = 4 }
public enum MaintenanceStatus { Open = 1, InProgress = 2, Resolved = 3, Closed = 4 }

public class MaintenanceRequest : PropertyEntity<Guid>
{
    public Guid RoomId { get; private set; }
    public string RoomNumber { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public MaintenancePriority Priority { get; private set; }
    public MaintenanceStatus Status { get; private set; }
    public string? ReportedByEmail { get; private set; }
    public string? AssignedToEmail { get; private set; }
    public string? ResolutionNotes { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    private MaintenanceRequest() { }

    public static MaintenanceRequest Create(
        Guid organizationId,
        Guid propertyId,
        Guid roomId,
        string roomNumber,
        string title,
        string? description,
        MaintenancePriority priority,
        string? reportedByEmail)
    {
        return new MaintenanceRequest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            RoomId = roomId,
            RoomNumber = roomNumber,
            Title = title.Trim(),
            Description = description?.Trim(),
            Priority = priority,
            Status = MaintenanceStatus.Open,
            ReportedByEmail = reportedByEmail,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public void UpdateStatus(
        MaintenanceStatus newStatus,
        string? assignedToEmail,
        string? resolutionNotes)
    {
        Status = newStatus;
        if (assignedToEmail != null) AssignedToEmail = assignedToEmail;
        if (resolutionNotes != null) ResolutionNotes = resolutionNotes;
        if (newStatus is MaintenanceStatus.Resolved or MaintenanceStatus.Closed)
            ResolvedAtUtc = DateTimeOffset.UtcNow;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void UpdateDetails(string title, string? description, MaintenancePriority priority)
    {
        Title = title.Trim();
        Description = description?.Trim();
        Priority = priority;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }
}

using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Inventory;

public class Room : PropertyEntity<Guid>
{
    public Guid? BuildingId { get; private set; }
    public Guid? FloorId { get; private set; }
    public Guid RoomTypeId { get; private set; }

    public string RoomNumber { get; private set; } = string.Empty;
    public string? DisplayName { get; private set; }
    public int Capacity { get; private set; } = 2;
    public RoomOperationalStatus OperationalStatus { get; private set; } = RoomOperationalStatus.Clean;
    public bool IsActive { get; private set; } = true;
    public string? Notes { get; private set; }

    private readonly List<RoomStatusHistory> _statusHistories = [];
    public IReadOnlyCollection<RoomStatusHistory> StatusHistories => _statusHistories.AsReadOnly();

    private Room() { } // EF Core

    public static Room Create(
        Guid organizationId,
        Guid propertyId,
        Guid roomTypeId,
        string roomNumber,
        Guid? buildingId = null,
        Guid? floorId = null,
        string? displayName = null,
        int capacity = 2,
        RoomOperationalStatus operationalStatus = RoomOperationalStatus.Clean,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomNumber);

        var room = new Room
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            RoomTypeId = roomTypeId,
            RoomNumber = roomNumber.Trim().ToUpperInvariant(),
            BuildingId = buildingId,
            FloorId = floorId,
            DisplayName = displayName?.Trim(),
            Capacity = Math.Max(1, capacity),
            OperationalStatus = operationalStatus,
            IsActive = true,
            Notes = notes?.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        return room;
    }

    public RoomStatusHistory UpdateOperationalStatus(
        RoomOperationalStatus newStatus,
        string? notes,
        Guid? actorId,
        string? actorEmail)
    {
        var oldStatus = OperationalStatus;
        OperationalStatus = newStatus;
        Touch();

        var history = RoomStatusHistory.Create(
            organizationId: OrganizationId,
            propertyId: PropertyId,
            roomId: Id,
            fromStatus: oldStatus,
            toStatus: newStatus,
            notes: notes,
            changedByUserId: actorId,
            changedByUserEmail: actorEmail
        );

        _statusHistories.Add(history);
        return history;
    }

    public void UpdateDetails(
        Guid roomTypeId,
        string roomNumber,
        Guid? buildingId,
        Guid? floorId,
        string? displayName,
        int capacity,
        string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomNumber);

        RoomTypeId = roomTypeId;
        RoomNumber = roomNumber.Trim().ToUpperInvariant();
        BuildingId = buildingId;
        FloorId = floorId;
        DisplayName = displayName?.Trim();
        Capacity = Math.Max(1, capacity);
        Notes = notes?.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }
}

public class RoomStatusHistory : PropertyEntity<Guid>
{
    public Guid RoomId { get; private set; }
    public RoomOperationalStatus FromStatus { get; private set; }
    public RoomOperationalStatus ToStatus { get; private set; }
    public string? Notes { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public string? ChangedByUserEmail { get; private set; }

    private RoomStatusHistory() { } // EF Core

    public static RoomStatusHistory Create(
        Guid organizationId,
        Guid propertyId,
        Guid roomId,
        RoomOperationalStatus fromStatus,
        RoomOperationalStatus toStatus,
        string? notes,
        Guid? changedByUserId,
        string? changedByUserEmail)
    {
        return new RoomStatusHistory
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            RoomId = roomId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Notes = notes?.Trim(),
            ChangedByUserId = changedByUserId,
            ChangedByUserEmail = changedByUserEmail,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }
}

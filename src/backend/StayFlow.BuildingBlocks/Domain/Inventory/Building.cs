using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Inventory;

public class Building : PropertyEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public int NumberOfFloors { get; private set; } = 1;
    public bool IsActive { get; private set; } = true;

    private readonly List<Floor> _floors = [];
    public IReadOnlyCollection<Floor> Floors => _floors.AsReadOnly();

    private Building() { } // EF Core

    public static Building Create(Guid organizationId, Guid propertyId, string name, string code, int numberOfFloors = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new Building
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            NumberOfFloors = Math.Max(1, numberOfFloors),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Update(string name, string code, int numberOfFloors)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        NumberOfFloors = Math.Max(1, numberOfFloors);
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

public class Floor : PropertyEntity<Guid>
{
    public Guid? BuildingId { get; private set; }
    public int FloorNumber { get; private set; }
    public string Name { get; private set; } = string.Empty;

    private Floor() { } // EF Core

    public static Floor Create(Guid organizationId, Guid propertyId, Guid? buildingId, int floorNumber, string? name = null)
    {
        return new Floor
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            BuildingId = buildingId,
            FloorNumber = floorNumber,
            Name = string.IsNullOrWhiteSpace(name) ? $"Floor {floorNumber}" : name.Trim(),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Touch();
    }
}

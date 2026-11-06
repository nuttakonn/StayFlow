using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Inventory;

public class RoomType : PropertyEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal BaseRate { get; private set; }
    public int StandardOccupancy { get; private set; } = 2;
    public int MaxOccupancy { get; private set; } = 2;
    public int MaxAdults { get; private set; } = 2;
    public int MaxChildren { get; private set; } = 1;
    public string? AmenitiesJson { get; private set; }
    public bool IsActive { get; private set; } = true;

    private RoomType() { } // EF Core

    public static RoomType Create(
        Guid organizationId,
        Guid propertyId,
        string name,
        string code,
        decimal baseRate,
        int standardOccupancy = 2,
        int maxOccupancy = 2,
        int maxAdults = 2,
        int maxChildren = 1,
        string? description = null,
        string? amenitiesJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new RoomType
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            BaseRate = Math.Max(0, baseRate),
            StandardOccupancy = Math.Max(1, standardOccupancy),
            MaxOccupancy = Math.Max(1, maxOccupancy),
            MaxAdults = Math.Max(1, maxAdults),
            MaxChildren = Math.Max(0, maxChildren),
            Description = description?.Trim(),
            AmenitiesJson = amenitiesJson,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        string name,
        string code,
        decimal baseRate,
        int standardOccupancy,
        int maxOccupancy,
        int maxAdults,
        int maxChildren,
        string? description,
        string? amenitiesJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        Name = name.Trim();
        Code = code.Trim().ToUpperInvariant();
        BaseRate = Math.Max(0, baseRate);
        StandardOccupancy = Math.Max(1, standardOccupancy);
        MaxOccupancy = Math.Max(1, maxOccupancy);
        MaxAdults = Math.Max(1, maxAdults);
        MaxChildren = Math.Max(0, maxChildren);
        Description = description?.Trim();
        AmenitiesJson = amenitiesJson;
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

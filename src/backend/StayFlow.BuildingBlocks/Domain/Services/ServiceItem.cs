using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Services;

public enum ServiceCategory { Meal = 0, Spa = 1, Transfer = 2, Other = 3 }

public class ServiceItem : PropertyEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public string Unit { get; private set; } = string.Empty;
    public ServiceCategory Category { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ServiceItem() { }

    public static ServiceItem Create(
        Guid organizationId,
        Guid propertyId,
        string name,
        string? description,
        decimal price,
        string unit,
        ServiceCategory category) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Name = name.Trim(),
            Description = description?.Trim(),
            Price = price,
            Unit = unit.Trim(),
            Category = category
        };

    public void Update(string name, string? description, decimal price, string unit, ServiceCategory category)
    {
        Name = name.Trim();
        Description = description?.Trim();
        Price = price;
        Unit = unit.Trim();
        Category = category;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAtUtc = DateTimeOffset.UtcNow; }
}

using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Services;

public enum MealPlanType { RoomOnly = 0, BedAndBreakfast = 1, HalfBoard = 2, FullBoard = 3 }

public class Package : PropertyEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public MealPlanType MealPlan { get; private set; }
    // Additional cost per night on top of base room rate
    public decimal RateAddOn { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<PackageItem> _items = [];
    public IReadOnlyCollection<PackageItem> Items => _items.AsReadOnly();

    private Package() { }

    public static Package Create(
        Guid organizationId,
        Guid propertyId,
        string name,
        string? description,
        MealPlanType mealPlan,
        decimal rateAddOn,
        IEnumerable<(Guid serviceItemId, int quantity, string? notes)>? items)
    {
        var pkg = new Package
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Name = name.Trim(),
            Description = description?.Trim(),
            MealPlan = mealPlan,
            RateAddOn = rateAddOn
        };

        if (items != null)
            foreach (var (sid, qty, notes) in items)
                pkg._items.Add(PackageItem.Create(pkg.Id, sid, qty, notes));

        return pkg;
    }

    public void Update(string name, string? description, MealPlanType mealPlan, decimal rateAddOn,
        IEnumerable<(Guid serviceItemId, int quantity, string? notes)>? items)
    {
        Name = name.Trim();
        Description = description?.Trim();
        MealPlan = mealPlan;
        RateAddOn = rateAddOn;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        _items.Clear();
        if (items != null)
            foreach (var (sid, qty, notes) in items)
                _items.Add(PackageItem.Create(Id, sid, qty, notes));
    }

    public void Deactivate() { IsActive = false; UpdatedAtUtc = DateTimeOffset.UtcNow; }
}

public class PackageItem : Entity<Guid>
{
    public Guid PackageId { get; private set; }
    public Guid ServiceItemId { get; private set; }
    public int Quantity { get; private set; }
    public string? Notes { get; private set; }

    private PackageItem() { }

    public static PackageItem Create(Guid packageId, Guid serviceItemId, int quantity, string? notes) =>
        new()
        {
            Id = Guid.NewGuid(),
            PackageId = packageId,
            ServiceItemId = serviceItemId,
            Quantity = quantity > 0 ? quantity : 1,
            Notes = notes?.Trim()
        };
}

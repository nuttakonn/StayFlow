using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Rates;

public enum DiscountType { Percentage = 0, FixedAmount = 1, FreeNight = 2 }

public class Coupon : PropertyEntity<Guid>
{
    public string Code { get; private set; } = string.Empty;
    public DiscountType DiscountType { get; private set; }
    public decimal Value { get; private set; }
    public int? MaxUses { get; private set; }
    public int UsedCount { get; private set; }
    public DateOnly? ValidFrom { get; private set; }
    public DateOnly? ValidTo { get; private set; }
    public string? EligibleRoomTypeIdsJson { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    private Coupon() { }

    public static Coupon Create(
        Guid organizationId,
        Guid propertyId,
        string code,
        DiscountType discountType,
        decimal value,
        int? maxUses,
        DateOnly? validFrom,
        DateOnly? validTo,
        IEnumerable<Guid>? eligibleRoomTypeIds)
    {
        var ids = eligibleRoomTypeIds?.ToArray() ?? [];
        return new Coupon
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Code = code.Trim().ToUpperInvariant(),
            DiscountType = discountType,
            Value = value,
            MaxUses = maxUses,
            ValidFrom = validFrom,
            ValidTo = validTo,
            EligibleRoomTypeIdsJson = ids.Length > 0
                ? System.Text.Json.JsonSerializer.Serialize(ids)
                : null
        };
    }

    public bool IsValid(DateOnly today) =>
        IsActive &&
        (ValidFrom == null || ValidFrom <= today) &&
        (ValidTo == null || ValidTo >= today) &&
        (MaxUses == null || UsedCount < MaxUses);

    public string? InvalidReason(DateOnly today)
    {
        if (!IsActive) return "Coupon is inactive.";
        if (ValidFrom != null && ValidFrom > today) return "Coupon is not yet valid.";
        if (ValidTo != null && ValidTo < today) return "Coupon has expired.";
        if (MaxUses != null && UsedCount >= MaxUses) return "Coupon has reached its maximum uses.";
        return null;
    }

    public decimal CalculateDiscount(decimal baseAmount) =>
        DiscountType switch
        {
            DiscountType.Percentage => Math.Round(baseAmount * Value / 100m, 2),
            DiscountType.FixedAmount => Math.Min(Value, baseAmount),
            _ => 0m
        };

    public void IncrementUsage() => UsedCount++;

    public void Deactivate() => IsActive = false;
}

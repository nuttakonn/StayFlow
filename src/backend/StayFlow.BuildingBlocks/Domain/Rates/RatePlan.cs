using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Rates;

public enum RateType { PerNight = 0, Weekly = 1, Monthly = 2 }
public enum DayType { Weekday = 0, Weekend = 1, PublicHoliday = 2, Seasonal = 3 }

public class RatePlan : PropertyEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public decimal BaseRate { get; private set; }
    public string Currency { get; private set; } = "THB";
    public RateType RateType { get; private set; } = RateType.PerNight;
    public bool IsPublic { get; private set; } = true;
    public bool IsActive { get; private set; } = true;
    public Guid? RoomTypeId { get; private set; }

    private readonly List<RateRule> _rules = [];
    public IReadOnlyCollection<RateRule> Rules => _rules.AsReadOnly();

    public RatePlanRestriction? Restriction { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; private set; }

    private RatePlan() { }

    public static RatePlan Create(
        Guid organizationId,
        Guid propertyId,
        string name,
        string code,
        decimal baseRate,
        string currency,
        RateType rateType,
        bool isPublic,
        Guid? roomTypeId,
        IEnumerable<(DayType dayType, decimal? rateMultiplier, decimal? flatAdjustment,
                     DateOnly? seasonStart, DateOnly? seasonEnd)>? rules,
        (int? minNights, int? maxAdvanceDays, IEnumerable<DateOnly>? blackoutDates)? restriction)
    {
        var plan = new RatePlan
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            BaseRate = baseRate,
            Currency = currency,
            RateType = rateType,
            IsPublic = isPublic,
            RoomTypeId = roomTypeId
        };

        if (rules != null)
            foreach (var r in rules)
                plan._rules.Add(RateRule.Create(plan.Id, r.dayType, r.rateMultiplier,
                    r.flatAdjustment, r.seasonStart, r.seasonEnd));

        if (restriction.HasValue)
            plan.Restriction = RatePlanRestriction.Create(plan.Id,
                restriction.Value.minNights, restriction.Value.maxAdvanceDays,
                restriction.Value.blackoutDates);

        return plan;
    }

    public void Update(
        string name,
        decimal baseRate,
        string currency,
        bool isPublic,
        IEnumerable<(DayType dayType, decimal? rateMultiplier, decimal? flatAdjustment,
                     DateOnly? seasonStart, DateOnly? seasonEnd)>? rules,
        (int? minNights, int? maxAdvanceDays, IEnumerable<DateOnly>? blackoutDates)? restriction)
    {
        Name = name.Trim();
        BaseRate = baseRate;
        Currency = currency;
        IsPublic = isPublic;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        _rules.Clear();
        if (rules != null)
            foreach (var r in rules)
                _rules.Add(RateRule.Create(Id, r.dayType, r.rateMultiplier,
                    r.flatAdjustment, r.seasonStart, r.seasonEnd));

        Restriction = restriction.HasValue
            ? RatePlanRestriction.Create(Id, restriction.Value.minNights,
                restriction.Value.maxAdvanceDays, restriction.Value.blackoutDates)
            : null;
    }

    public void Deactivate() { IsActive = false; UpdatedAtUtc = DateTimeOffset.UtcNow; }
}

public class RateRule : Entity<Guid>
{
    public Guid RatePlanId { get; private set; }
    public DayType DayType { get; private set; }
    public decimal? RateMultiplier { get; private set; }
    public decimal? FlatAdjustment { get; private set; }
    public DateOnly? SeasonStartDate { get; private set; }
    public DateOnly? SeasonEndDate { get; private set; }

    private RateRule() { }

    public static RateRule Create(Guid ratePlanId, DayType dayType,
        decimal? multiplier, decimal? flat, DateOnly? seasonStart, DateOnly? seasonEnd) =>
        new()
        {
            Id = Guid.NewGuid(),
            RatePlanId = ratePlanId,
            DayType = dayType,
            RateMultiplier = multiplier,
            FlatAdjustment = flat,
            SeasonStartDate = seasonStart,
            SeasonEndDate = seasonEnd
        };
}

public class RatePlanRestriction : Entity<Guid>
{
    public Guid RatePlanId { get; private set; }
    public int? MinNights { get; private set; }
    public int? MaxAdvanceDays { get; private set; }
    public string? BlackoutDatesJson { get; private set; }

    private RatePlanRestriction() { }

    public static RatePlanRestriction Create(Guid ratePlanId, int? minNights,
        int? maxAdvanceDays, IEnumerable<DateOnly>? blackoutDates)
    {
        var dates = blackoutDates?.Select(d => d.ToString("yyyy-MM-dd")).ToArray() ?? [];
        return new RatePlanRestriction
        {
            Id = Guid.NewGuid(),
            RatePlanId = ratePlanId,
            MinNights = minNights,
            MaxAdvanceDays = maxAdvanceDays,
            BlackoutDatesJson = dates.Length > 0
                ? System.Text.Json.JsonSerializer.Serialize(dates)
                : null
        };
    }
}

using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Properties;

public enum PropertyType
{
    Hotel = 1,
    Apartment = 2,
    Hybrid = 3
}

public class Property : TenantEntity<Guid>
{
    public string Name { get; private set; } = string.Empty;
    public string Code { get; private set; } = string.Empty;
    public PropertyType BusinessType { get; private set; } = PropertyType.Hotel;
    public string IanaTimeZone { get; private set; } = "Asia/Bangkok";
    public string DefaultCurrency { get; private set; } = "THB";
    public string? Address { get; private set; }
    public string? ContactPhone { get; private set; }
    public string? ContactEmail { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Operational Settings
    public TimeOnly CheckInTime { get; private set; } = new(14, 0);
    public TimeOnly CheckOutTime { get; private set; } = new(12, 0);

    // Thai compliance — must be explicitly enabled per property
    public bool EnableThaiCompliance { get; private set; } = false;

    private Property() { } // EF Core

    public static Property Create(
        Guid organizationId,
        string name,
        string code,
        PropertyType businessType = PropertyType.Hotel,
        string ianaTimeZone = "Asia/Bangkok",
        string defaultCurrency = "THB",
        string? address = null,
        string? contactPhone = null,
        string? contactEmail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        return new Property
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            BusinessType = businessType,
            IanaTimeZone = string.IsNullOrWhiteSpace(ianaTimeZone) ? "Asia/Bangkok" : ianaTimeZone.Trim(),
            DefaultCurrency = string.IsNullOrWhiteSpace(defaultCurrency) ? "THB" : defaultCurrency.Trim().ToUpperInvariant(),
            Address = address?.Trim(),
            ContactPhone = contactPhone?.Trim(),
            ContactEmail = contactEmail?.Trim(),
            IsActive = true,
            CheckInTime = new TimeOnly(14, 0),
            CheckOutTime = new TimeOnly(12, 0),
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateDetails(
        string name,
        string? address,
        string? contactPhone,
        string? contactEmail,
        string ianaTimeZone,
        string defaultCurrency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Address = address?.Trim();
        ContactPhone = contactPhone?.Trim();
        ContactEmail = contactEmail?.Trim();
        IanaTimeZone = ianaTimeZone.Trim();
        DefaultCurrency = defaultCurrency.Trim().ToUpperInvariant();
        Touch();
    }

    public void ConfigureCheckInOutPolicy(TimeOnly checkInTime, TimeOnly checkOutTime)
    {
        CheckInTime = checkInTime;
        CheckOutTime = checkOutTime;
        Touch();
    }

    public void SetThaiComplianceMode(bool enabled)
    {
        EnableThaiCompliance = enabled;
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

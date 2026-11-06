using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Apartments;

public class Tenant : TenantEntity<Guid>
{
    public Guid PropertyId { get; private set; }
    public string FullName { get; private set; } = string.Empty;
    public string NationalIdOrPassport { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Tenant() { } // EF Core

    public static Tenant Create(
        Guid organizationId,
        Guid propertyId,
        string fullName,
        string nationalIdOrPassport,
        string phoneNumber,
        string? email = null,
        string? emergencyContactName = null,
        string? emergencyContactPhone = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        return new Tenant
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            FullName = fullName.Trim(),
            NationalIdOrPassport = nationalIdOrPassport?.Trim() ?? string.Empty,
            PhoneNumber = phoneNumber.Trim(),
            Email = email?.Trim(),
            EmergencyContactName = emergencyContactName?.Trim(),
            EmergencyContactPhone = emergencyContactPhone?.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        string fullName,
        string nationalIdOrPassport,
        string phoneNumber,
        string? email,
        string? emergencyContactName,
        string? emergencyContactPhone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        FullName = fullName.Trim();
        NationalIdOrPassport = nationalIdOrPassport?.Trim() ?? string.Empty;
        PhoneNumber = phoneNumber.Trim();
        Email = email?.Trim();
        EmergencyContactName = emergencyContactName?.Trim();
        EmergencyContactPhone = emergencyContactPhone?.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }
}

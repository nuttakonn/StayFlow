using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Parties;

public class Guest : TenantEntity<Guid>
{
    public string FullName { get; private set; } = string.Empty;
    public string? NationalId { get; private set; }
    public string? PassportNumber { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Nationality { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Guest() { } // EF Core

    public static Guest Create(
        Guid organizationId,
        string fullName,
        string? email = null,
        string? phone = null,
        string? nationalId = null,
        string? passportNumber = null,
        string? nationality = null,
        string? notes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new Guest
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            FullName = fullName.Trim(),
            Email = email?.Trim().ToLowerInvariant(),
            Phone = phone?.Trim(),
            NationalId = nationalId?.Trim(),
            PassportNumber = passportNumber?.Trim(),
            Nationality = nationality?.Trim(),
            Notes = notes?.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(
        string fullName,
        string? email,
        string? phone,
        string? nationalId,
        string? passportNumber,
        string? nationality,
        string? notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        Email = email?.Trim().ToLowerInvariant();
        Phone = phone?.Trim();
        NationalId = nationalId?.Trim();
        PassportNumber = passportNumber?.Trim();
        Nationality = nationality?.Trim();
        Notes = notes?.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }
}

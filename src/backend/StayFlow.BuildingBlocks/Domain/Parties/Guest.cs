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

    // Thai compliance fields (required for R.S.3, R.S.4, TM.30)
    public DateOnly? DateOfBirth { get; private set; }
    public string? VisaType { get; private set; }       // e.g. "Tourist", "Business", "Transit", "Exempt"
    public string? CountryOfOrigin { get; private set; } // ISO country name

    private Guest() { } // EF Core

    public static Guest Create(
        Guid organizationId,
        string fullName,
        string? email = null,
        string? phone = null,
        string? nationalId = null,
        string? passportNumber = null,
        string? nationality = null,
        string? notes = null,
        DateOnly? dateOfBirth = null,
        string? visaType = null,
        string? countryOfOrigin = null)
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
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DateOfBirth = dateOfBirth,
            VisaType = visaType?.Trim(),
            CountryOfOrigin = countryOfOrigin?.Trim()
        };
    }

    public void UpdateProfile(
        string fullName,
        string? email,
        string? phone,
        string? nationalId,
        string? passportNumber,
        string? nationality,
        string? notes,
        DateOnly? dateOfBirth = null,
        string? visaType = null,
        string? countryOfOrigin = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        FullName = fullName.Trim();
        Email = email?.Trim().ToLowerInvariant();
        Phone = phone?.Trim();
        NationalId = nationalId?.Trim();
        PassportNumber = passportNumber?.Trim();
        Nationality = nationality?.Trim();
        Notes = notes?.Trim();
        DateOfBirth = dateOfBirth;
        VisaType = visaType?.Trim();
        CountryOfOrigin = countryOfOrigin?.Trim();
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }
}

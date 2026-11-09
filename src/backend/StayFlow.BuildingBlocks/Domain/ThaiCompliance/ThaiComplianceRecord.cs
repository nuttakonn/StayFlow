using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.ThaiCompliance;

/// <summary>
/// Immutable per-stay snapshot of guest data required by Thai law (R.S.3, R.S.4, TM.30).
/// Created at or after check-in. Corrections go through ThaiComplianceAmendment.
/// Thai guests: NationalId required. Foreign guests: PassportNumber + VisaType required.
/// </summary>
public class ThaiComplianceRecord : PropertyEntity<Guid>
{
    public Guid ReservationId { get; private set; }
    public Guid GuestId { get; private set; }

    // Snapshot of guest data at the time the record is created (immutable)
    public string GuestFullName { get; private set; } = string.Empty;
    public string? NationalId { get; private set; }       // Thai citizens
    public string? PassportNumber { get; private set; }   // Foreign guests
    public string? Nationality { get; private set; }
    public DateOnly? DateOfBirth { get; private set; }
    public string? VisaType { get; private set; }
    public string? CountryOfOrigin { get; private set; }
    public bool IsForeigner { get; private set; }

    // Stay details snapshot
    public string? RoomNumber { get; private set; }
    public DateOnly CheckInDate { get; private set; }
    public DateOnly CheckOutDate { get; private set; }
    public int Adults { get; private set; }

    public bool IsAmended { get; private set; } = false;
    public DateTimeOffset RecordedAtUtc { get; private set; }

    private readonly List<ThaiComplianceAmendment> _amendments = [];
    public IReadOnlyCollection<ThaiComplianceAmendment> Amendments => _amendments.AsReadOnly();

    private ThaiComplianceRecord() { }

    public static ThaiComplianceRecord Create(
        Guid organizationId,
        Guid propertyId,
        Guid reservationId,
        Guid guestId,
        string guestFullName,
        string? nationalId,
        string? passportNumber,
        string? nationality,
        DateOnly? dateOfBirth,
        string? visaType,
        string? countryOfOrigin,
        string? roomNumber,
        DateOnly checkInDate,
        DateOnly checkOutDate,
        int adults)
    {
        var isForeigner = string.IsNullOrWhiteSpace(nationalId) && !string.IsNullOrWhiteSpace(passportNumber);

        return new ThaiComplianceRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            ReservationId = reservationId,
            GuestId = guestId,
            GuestFullName = guestFullName.Trim(),
            NationalId = nationalId?.Trim(),
            PassportNumber = passportNumber?.Trim(),
            Nationality = nationality?.Trim(),
            DateOfBirth = dateOfBirth,
            VisaType = visaType?.Trim(),
            CountryOfOrigin = countryOfOrigin?.Trim(),
            IsForeigner = isForeigner,
            RoomNumber = roomNumber?.Trim(),
            CheckInDate = checkInDate,
            CheckOutDate = checkOutDate,
            Adults = adults,
            RecordedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public ThaiComplianceAmendment Amend(
        string reason,
        string amendedByEmail,
        string? newNationalId,
        string? newPassportNumber,
        string? newNationality,
        DateOnly? newDateOfBirth,
        string? newVisaType,
        string? newCountryOfOrigin)
    {
        var amendment = ThaiComplianceAmendment.Create(
            Id,
            reason,
            amendedByEmail,
            NationalId, newNationalId,
            PassportNumber, newPassportNumber,
            Nationality, newNationality,
            DateOfBirth, newDateOfBirth,
            VisaType, newVisaType,
            CountryOfOrigin, newCountryOfOrigin);

        // Update snapshot with corrected values
        NationalId = newNationalId?.Trim();
        PassportNumber = newPassportNumber?.Trim();
        Nationality = newNationality?.Trim();
        DateOfBirth = newDateOfBirth;
        VisaType = newVisaType?.Trim();
        CountryOfOrigin = newCountryOfOrigin?.Trim();
        IsForeigner = string.IsNullOrWhiteSpace(NationalId) && !string.IsNullOrWhiteSpace(PassportNumber);
        IsAmended = true;
        UpdatedAtUtc = DateTimeOffset.UtcNow;

        _amendments.Add(amendment);
        return amendment;
    }
}

public class ThaiComplianceAmendment : Entity<Guid>
{
    public Guid RecordId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string AmendedByEmail { get; private set; } = string.Empty;

    // Before / after for each amendable field (null = unchanged)
    public string? PrevNationalId { get; private set; }
    public string? NewNationalId { get; private set; }
    public string? PrevPassportNumber { get; private set; }
    public string? NewPassportNumber { get; private set; }
    public string? PrevNationality { get; private set; }
    public string? NewNationality { get; private set; }
    public DateOnly? PrevDateOfBirth { get; private set; }
    public DateOnly? NewDateOfBirth { get; private set; }
    public string? PrevVisaType { get; private set; }
    public string? NewVisaType { get; private set; }
    public string? PrevCountryOfOrigin { get; private set; }
    public string? NewCountryOfOrigin { get; private set; }

    private ThaiComplianceAmendment() { }

    internal static ThaiComplianceAmendment Create(
        Guid recordId,
        string reason,
        string amendedByEmail,
        string? prevNationalId, string? newNationalId,
        string? prevPassportNumber, string? newPassportNumber,
        string? prevNationality, string? newNationality,
        DateOnly? prevDateOfBirth, DateOnly? newDateOfBirth,
        string? prevVisaType, string? newVisaType,
        string? prevCountryOfOrigin, string? newCountryOfOrigin) =>
        new()
        {
            Id = Guid.NewGuid(),
            RecordId = recordId,
            Reason = reason.Trim(),
            AmendedByEmail = amendedByEmail.Trim(),
            PrevNationalId = prevNationalId,
            NewNationalId = newNationalId,
            PrevPassportNumber = prevPassportNumber,
            NewPassportNumber = newPassportNumber,
            PrevNationality = prevNationality,
            NewNationality = newNationality,
            PrevDateOfBirth = prevDateOfBirth,
            NewDateOfBirth = newDateOfBirth,
            PrevVisaType = prevVisaType,
            NewVisaType = newVisaType,
            PrevCountryOfOrigin = prevCountryOfOrigin,
            NewCountryOfOrigin = newCountryOfOrigin
        };
}

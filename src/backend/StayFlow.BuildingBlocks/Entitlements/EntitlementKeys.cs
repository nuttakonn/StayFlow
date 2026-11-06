namespace StayFlow.BuildingBlocks.Entitlements;

/// <summary>
/// Stable capability keys (CLAUDE.md section 21.2). Business modules must check these
/// server-side via IEntitlementService instead of hardcoding plan names; frontend
/// visibility is never a security boundary. Keys are deliberately plain strings (not an
/// enum) so a new plan can grant a key this binary doesn't recognize yet without a
/// redeploy, and so the plan catalog stays data, not code.
/// </summary>
public static class EntitlementKeys
{
    public const string HotelReservations = "hotel.reservations";
    public const string HotelHousekeeping = "hotel.housekeeping";
    public const string HotelNightAudit = "hotel.nightAudit";
    public const string ApartmentRentals = "apartment.rentals";
    public const string ApartmentUtilities = "apartment.utilities";
    public const string FinanceInvoices = "finance.invoices";
    public const string ReportingAdvanced = "reporting.advanced";
    public const string BrandingOrganization = "branding.organization";
    public const string BrandingProperty = "branding.property";
    public const string BrandingCustomDomain = "branding.customDomain";

    /// <summary>All boolean capability keys. Numeric limits (users.max, properties.max) are
    /// modeled as typed fields on SubscriptionPlan/EffectiveEntitlements, not as keys here,
    /// since they carry a value rather than being on/off.</summary>
    public static readonly string[] All =
    [
        HotelReservations, HotelHousekeeping, HotelNightAudit,
        ApartmentRentals, ApartmentUtilities,
        FinanceInvoices, ReportingAdvanced,
        BrandingOrganization, BrandingProperty, BrandingCustomDomain
    ];
}

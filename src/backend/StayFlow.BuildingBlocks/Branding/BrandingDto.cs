namespace StayFlow.BuildingBlocks.Branding;

public record BrandingDto(
    string DisplayName,
    string? LogoUrl,
    string? FaviconUrl,
    string PrimaryColor,
    string SecondaryColor,
    string? LoginTitle,
    string? LoginWelcomeText,
    string? FooterText,
    string? SupportEmail,
    string? SupportPhone,
    bool ShowVendorAttribution
)
{
    public static BrandingDto PlatformDefault => new(
        DisplayName: "StayFlow PMS",
        LogoUrl: null,
        FaviconUrl: null,
        PrimaryColor: "#1E40AF", // Tailwind blue-800
        SecondaryColor: "#0EA5E9", // Tailwind sky-500
        LoginTitle: "Welcome to StayFlow PMS",
        LoginWelcomeText: "Cloud Property Management System for Hotels & Apartments",
        FooterText: "Powered by StayFlow PMS",
        SupportEmail: "support@stayflow.com",
        SupportPhone: null,
        ShowVendorAttribution: true
    );
}

public interface IBrandingResolver
{
    Task<BrandingDto> ResolveEffectiveBrandingAsync(
        Guid? organizationId,
        Guid? propertyId,
        CancellationToken cancellationToken = default);
}

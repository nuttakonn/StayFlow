using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Branding;

public class BrandingSettings : Entity<Guid>
{
    public Guid OrganizationId { get; private set; }
    public Guid? PropertyId { get; private set; } // Null if organization-level, set if property override

    public string DisplayName { get; private set; } = string.Empty;
    public string? LogoUrl { get; private set; }
    public string? FaviconUrl { get; private set; }
    public string PrimaryColor { get; private set; } = "#1E40AF";
    public string SecondaryColor { get; private set; } = "#0EA5E9";
    public string? LoginTitle { get; private set; }
    public string? LoginWelcomeText { get; private set; }
    public string? FooterText { get; private set; }
    public string? SupportEmail { get; private set; }
    public string? SupportPhone { get; private set; }
    public bool ShowVendorAttribution { get; private set; } = true;

    private BrandingSettings() { } // EF Core

    public static BrandingSettings Create(
        Guid organizationId,
        Guid? propertyId,
        string displayName,
        string? logoUrl = null,
        string? faviconUrl = null,
        string? primaryColor = null,
        string? secondaryColor = null,
        string? loginTitle = null,
        string? loginWelcomeText = null,
        string? footerText = null,
        string? supportEmail = null,
        string? supportPhone = null,
        bool showVendorAttribution = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        return new BrandingSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            DisplayName = displayName.Trim(),
            LogoUrl = logoUrl?.Trim(),
            FaviconUrl = faviconUrl?.Trim(),
            PrimaryColor = string.IsNullOrWhiteSpace(primaryColor) ? "#1E40AF" : primaryColor.Trim(),
            SecondaryColor = string.IsNullOrWhiteSpace(secondaryColor) ? "#0EA5E9" : secondaryColor.Trim(),
            LoginTitle = loginTitle?.Trim(),
            LoginWelcomeText = loginWelcomeText?.Trim(),
            FooterText = footerText?.Trim(),
            SupportEmail = supportEmail?.Trim(),
            SupportPhone = supportPhone?.Trim(),
            ShowVendorAttribution = showVendorAttribution,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void Update(
        string displayName,
        string? logoUrl,
        string? faviconUrl,
        string? primaryColor,
        string? secondaryColor,
        string? loginTitle,
        string? loginWelcomeText,
        string? footerText,
        string? supportEmail,
        string? supportPhone,
        bool showVendorAttribution)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        DisplayName = displayName.Trim();
        LogoUrl = logoUrl?.Trim();
        FaviconUrl = faviconUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(primaryColor)) PrimaryColor = primaryColor.Trim();
        if (!string.IsNullOrWhiteSpace(secondaryColor)) SecondaryColor = secondaryColor.Trim();
        LoginTitle = loginTitle?.Trim();
        LoginWelcomeText = loginWelcomeText?.Trim();
        FooterText = footerText?.Trim();
        SupportEmail = supportEmail?.Trim();
        SupportPhone = supportPhone?.Trim();
        ShowVendorAttribution = showVendorAttribution;
        Touch();
    }
}

using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Branding;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Branding;

public class BrandingResolver : IBrandingResolver
{
    private readonly StayFlowDbContext _dbContext;

    public BrandingResolver(StayFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<BrandingDto> ResolveEffectiveBrandingAsync(
        Guid? organizationId,
        Guid? propertyId,
        CancellationToken cancellationToken = default)
    {
        // 1. Try Property-level override
        if (organizationId.HasValue && propertyId.HasValue)
        {
            var propertyBranding = await _dbContext.BrandingSettings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.OrganizationId == organizationId.Value && b.PropertyId == propertyId.Value, cancellationToken);

            if (propertyBranding != null)
            {
                return MapToDto(propertyBranding);
            }
        }

        // 2. Try Organization-level branding
        if (organizationId.HasValue)
        {
            var orgBranding = await _dbContext.BrandingSettings
                .AsNoTracking()
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(b => b.OrganizationId == organizationId.Value && b.PropertyId == null, cancellationToken);

            if (orgBranding != null)
            {
                return MapToDto(orgBranding);
            }
        }

        // 3. Fallback to platform default
        return BrandingDto.PlatformDefault;
    }

    private static BrandingDto MapToDto(BrandingSettings entity) => new(
        DisplayName: entity.DisplayName,
        LogoUrl: entity.LogoUrl,
        FaviconUrl: entity.FaviconUrl,
        PrimaryColor: entity.PrimaryColor,
        SecondaryColor: entity.SecondaryColor,
        LoginTitle: entity.LoginTitle,
        LoginWelcomeText: entity.LoginWelcomeText,
        FooterText: entity.FooterText,
        SupportEmail: entity.SupportEmail,
        SupportPhone: entity.SupportPhone,
        ShowVendorAttribution: entity.ShowVendorAttribution
    );
}

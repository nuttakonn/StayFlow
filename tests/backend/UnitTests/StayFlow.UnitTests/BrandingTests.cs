using StayFlow.BuildingBlocks.Branding;

namespace StayFlow.UnitTests;

public class BrandingTests
{
    [Fact]
    public void PlatformDefault_ShouldHaveExpectedValues()
    {
        var defaultBranding = BrandingDto.PlatformDefault;

        Assert.Equal("StayFlow PMS", defaultBranding.DisplayName);
        Assert.Equal("#1E40AF", defaultBranding.PrimaryColor);
        Assert.Equal("#0EA5E9", defaultBranding.SecondaryColor);
        Assert.True(defaultBranding.ShowVendorAttribution);
        Assert.Null(defaultBranding.LogoUrl);
    }
}

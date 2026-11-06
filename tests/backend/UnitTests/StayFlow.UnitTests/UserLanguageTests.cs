using StayFlow.BuildingBlocks.Domain.Identity;

namespace StayFlow.UnitTests;

/// <summary>
/// Unit tests for the per-user UI language preference (EPIC-49).
/// Language is an account-level setting, independent of the property locale.
/// </summary>
public class UserLanguageTests
{
    private static User MakeUser(string? language = null)
        => User.Create("staff@example.com", "hash", "Somchai P.", null, language);

    // ── SupportedLanguages ───────────────────────────────────────────────────

    [Theory]
    [InlineData("en")]
    [InlineData("th")]
    [InlineData("zh-CN")]
    public void IsSupported_TierOneAndTwoLanguages_ReturnsTrue(string code)
        => Assert.True(SupportedLanguages.IsSupported(code));

    [Theory]
    [InlineData("EN")]
    [InlineData("Th")]
    [InlineData("zh-cn")]
    public void IsSupported_IsCaseInsensitive(string code)
        => Assert.True(SupportedLanguages.IsSupported(code));

    [Theory]
    [InlineData("fr")]
    [InlineData("zh-TW")]
    [InlineData("")]
    [InlineData(null)]
    public void IsSupported_UnsupportedOrEmpty_ReturnsFalse(string? code)
        => Assert.False(SupportedLanguages.IsSupported(code));

    [Fact]
    public void Normalize_ReturnsCanonicalCasing()
    {
        // BCP-47 region subtags are conventionally upper-case; clients may send
        // "zh-cn" but we must persist "zh-CN" so the frontend resource key matches.
        Assert.Equal("zh-CN", SupportedLanguages.Normalize("zh-cn"));
        Assert.Equal("en", SupportedLanguages.Normalize("EN"));
        Assert.Equal("th", SupportedLanguages.Normalize("  th  "));
    }

    [Fact]
    public void Normalize_Unsupported_ReturnsNull()
        => Assert.Null(SupportedLanguages.Normalize("de"));

    [Fact]
    public void Default_IsEnglish()
        => Assert.Equal("en", SupportedLanguages.Default);

    // ── User.Create ──────────────────────────────────────────────────────────

    [Fact]
    public void Create_WithoutLanguage_DefaultsToEnglish()
        => Assert.Equal("en", MakeUser().PreferredLanguage);

    [Fact]
    public void Create_WithSupportedLanguage_UsesIt()
        => Assert.Equal("th", MakeUser("th").PreferredLanguage);

    [Fact]
    public void Create_WithUnsupportedLanguage_FallsBackToDefault()
    {
        // Registration must not fail because of an unrecognised Accept-Language;
        // the safe default is English.
        Assert.Equal("en", MakeUser("fr-CA").PreferredLanguage);
    }

    [Fact]
    public void Create_NormalizesCasing()
        => Assert.Equal("zh-CN", MakeUser("zh-cn").PreferredLanguage);

    // ── SetPreferredLanguage ─────────────────────────────────────────────────

    [Fact]
    public void SetPreferredLanguage_ToSupportedValue_Updates()
    {
        var user = MakeUser("en");
        user.SetPreferredLanguage("th");
        Assert.Equal("th", user.PreferredLanguage);
    }

    [Fact]
    public void SetPreferredLanguage_NormalizesCasing()
    {
        var user = MakeUser("en");
        user.SetPreferredLanguage("ZH-cn");
        Assert.Equal("zh-CN", user.PreferredLanguage);
    }

    [Fact]
    public void SetPreferredLanguage_ToUnsupportedValue_Throws()
    {
        // Unlike Create, an explicit change to an unsupported language is a
        // client error and must surface rather than silently fall back.
        var user = MakeUser("en");
        var ex = Assert.Throws<ArgumentException>(() => user.SetPreferredLanguage("ja"));
        Assert.Contains("ja", ex.Message);
        Assert.Equal("en", user.PreferredLanguage);
    }

    [Fact]
    public void SetPreferredLanguage_DoesNotAffectOtherProfileFields()
    {
        var user = MakeUser("en");
        user.SetPreferredLanguage("th");

        Assert.Equal("staff@example.com", user.Email);
        Assert.Equal("Somchai P.", user.FullName);
        Assert.True(user.IsActive);
    }
}

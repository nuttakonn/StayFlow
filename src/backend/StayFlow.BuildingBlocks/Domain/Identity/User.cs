using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Identity;

/// <summary>
/// Languages the back-office UI supports. Thai and English are tier-1 (fully
/// translated). Chinese Simplified is tier-2 — accepted and stored, with its
/// locale file filled in per EPIC-50.
/// </summary>
public static class SupportedLanguages
{
    public const string English = "en";
    public const string Thai = "th";
    public const string ChineseSimplified = "zh-CN";

    public const string Default = English;

    public static readonly IReadOnlyList<string> All = [English, Thai, ChineseSimplified];

    public static bool IsSupported(string? code) =>
        code is not null && All.Contains(code, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the canonically-cased supported code, or null when unsupported.
    /// Canonical casing matters because BCP-47 subtags are case-sensitive to
    /// some clients ("zh-CN", not "zh-cn").
    /// </summary>
    public static string? Normalize(string? code)
    {
        if (code is null) return null;
        var trimmed = code.Trim();
        return All.FirstOrDefault(l => string.Equals(l, trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public class User : Entity<Guid>
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public string? PhoneNumber { get; private set; }
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Per-user UI language (BCP-47). Independent of the property's locale and
    /// time zone: staff of different nationalities in one property each pick
    /// their own.
    /// </summary>
    public string PreferredLanguage { get; private set; } = SupportedLanguages.Default;

    private User() { } // EF Core

    public static User Create(
        string email,
        string passwordHash,
        string fullName,
        string? phoneNumber = null,
        string? preferredLanguage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);

        return new User
        {
            Id = Guid.NewGuid(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            FullName = fullName.Trim(),
            PhoneNumber = phoneNumber?.Trim(),
            PreferredLanguage = SupportedLanguages.Normalize(preferredLanguage) ?? SupportedLanguages.Default,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string fullName, string? phoneNumber)
    {
        FullName = fullName.Trim();
        PhoneNumber = phoneNumber?.Trim();
        Touch();
    }

    public void SetPreferredLanguage(string language)
    {
        var normalized = SupportedLanguages.Normalize(language)
            ?? throw new ArgumentException(
                $"Unsupported language '{language}'. Supported: {string.Join(", ", SupportedLanguages.All)}.",
                nameof(language));

        PreferredLanguage = normalized;
        Touch();
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }
}

public class RefreshToken : Entity<Guid>
{
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;
    public bool IsRevoked => RevokedAtUtc.HasValue;
    public bool IsActive => !IsRevoked && !IsExpired;

    private RefreshToken() { } // EF Core

    public static RefreshToken Create(Guid userId, string token, TimeSpan duration)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = token,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(duration)
        };
    }

    public void Revoke()
    {
        RevokedAtUtc = DateTimeOffset.UtcNow;
        Touch();
    }
}

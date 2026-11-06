using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.ChannelManager;

public enum ChannelProvider
{
    STAAH = 1,
    SiteMinder = 2,
    Roomcloud = 3,
    MyAllocator = 4
}

public class ChannelConnection : PropertyEntity<Guid>
{
    public ChannelProvider Provider { get; private set; }
    public string ProviderPropertyId { get; private set; } = string.Empty;
    public string EncryptedApiKey { get; private set; } = string.Empty;
    public string? EncryptedApiSecret { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset? LastSyncAtUtc { get; private set; }
    public string? LastSyncStatus { get; private set; }

    private ChannelConnection() { }

    public static ChannelConnection Create(
        Guid organizationId,
        Guid propertyId,
        ChannelProvider provider,
        string providerPropertyId,
        string encryptedApiKey,
        string? encryptedApiSecret = null)
    {
        if (string.IsNullOrWhiteSpace(providerPropertyId))
            throw new ArgumentException("Provider property ID is required.", nameof(providerPropertyId));
        if (string.IsNullOrWhiteSpace(encryptedApiKey))
            throw new ArgumentException("API key is required.", nameof(encryptedApiKey));

        return new ChannelConnection
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId,
            Provider = provider,
            ProviderPropertyId = providerPropertyId.Trim(),
            EncryptedApiKey = encryptedApiKey,
            EncryptedApiSecret = encryptedApiSecret,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public void Update(string providerPropertyId, string encryptedApiKey, string? encryptedApiSecret)
    {
        ProviderPropertyId = providerPropertyId.Trim();
        EncryptedApiKey = encryptedApiKey;
        EncryptedApiSecret = encryptedApiSecret;
        Touch();
    }

    public void RecordSync(bool success, string? statusMessage = null)
    {
        LastSyncAtUtc = DateTimeOffset.UtcNow;
        LastSyncStatus = success ? "Success" : (statusMessage ?? "Failed");
        Touch();
    }

    public void Deactivate() { IsActive = false; Touch(); }
    public void Activate() { IsActive = true; Touch(); }
}

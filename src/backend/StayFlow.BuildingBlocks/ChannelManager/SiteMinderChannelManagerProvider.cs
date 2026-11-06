using System.Security.Cryptography;
using System.Text;
using StayFlow.BuildingBlocks.Domain.ChannelManager;

namespace StayFlow.BuildingBlocks.ChannelManager;

/// <summary>
/// SiteMinder channel manager provider stub.
/// Replace HTTP call bodies with actual SiteMinder API contracts once
/// SiteMinder partner credentials and API documentation are obtained.
/// SiteMinder uses the OTA_HotelAvailNotifRQ / OTA_ResNotifRQ XML schema (similar to STAAH).
/// </summary>
public class SiteMinderChannelManagerProvider : IChannelManagerProvider
{
    private readonly IHttpClientFactory _httpClientFactory;

    public SiteMinderChannelManagerProvider(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public ChannelProvider Provider => ChannelProvider.SiteMinder;

    public async Task<bool> PushAvailabilityAsync(
        ChannelConnection connection,
        IReadOnlyList<AvailabilityUpdate> updates,
        CancellationToken ct = default)
    {
        // TODO: Call SiteMinder ARI Update API.
        // SiteMinder GDS uses SOAP/XML OTA_HotelAvailNotifRQ.
        // Endpoint: https://api.siteminder.com/ari/v1/update (verify from SiteMinder partner docs)
        await Task.CompletedTask;
        return true;
    }

    public Task<InboundBookingDto?> ParseInboundBookingAsync(
        string rawPayload,
        CancellationToken ct = default)
    {
        // TODO: Parse SiteMinder booking notification (OTA_ResNotifRQ XML format).
        return Task.FromResult<InboundBookingDto?>(null);
    }

    public bool ValidateWebhookSignature(
        string rawPayload,
        string? signatureHeader,
        string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        var key = Encoding.UTF8.GetBytes(webhookSecret);
        var data = Encoding.UTF8.GetBytes(rawPayload);
        var expected = Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader));
    }
}

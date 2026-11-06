using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using StayFlow.BuildingBlocks.Domain.ChannelManager;

namespace StayFlow.BuildingBlocks.ChannelManager;

/// <summary>
/// STAAH channel manager provider.
///
/// Inbound: Parses OTA_ResNotifRQ XML (OpenTravel Alliance standard, ver 1.0).
/// Outbound: Sends OTA_HotelAvailNotifRQ XML to STAAH ARI endpoint.
///
/// Replace <see cref="StaahBaseUrl"/> and the HTTP auth scheme once STAAH
/// partner credentials are obtained. The XML schema used here follows the
/// OTA 2003 / 2007 specification that STAAH documents for their API partners.
/// </summary>
public class StaahChannelManagerProvider : IChannelManagerProvider
{
    // Update when STAAH partner portal provides the actual endpoint.
    private const string StaahBaseUrl = "https://api.staah.com/v1";

    // OTA namespace used in STAAH-flavoured OTA XML
    private static readonly XNamespace OtaNs = "http://www.opentravel.org/OTA/2003/05";

    // AgeQualifyingCode values per OTA standard
    private const string AdultCode = "10";
    private const string ChildCode = "8";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<StaahChannelManagerProvider> _logger;

    public StaahChannelManagerProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<StaahChannelManagerProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public ChannelProvider Provider => ChannelProvider.STAAH;

    // ── Outbound: push availability / rates ──────────────────────────────────

    public async Task<bool> PushAvailabilityAsync(
        ChannelConnection connection,
        IReadOnlyList<AvailabilityUpdate> updates,
        CancellationToken ct = default)
    {
        if (updates.Count == 0) return true;

        var xml = BuildAvailNotifXml(connection.ProviderPropertyId, updates);

        try
        {
            var client = _httpClientFactory.CreateClient("staah");
            var content = new StringContent(xml, Encoding.UTF8, "application/xml");
            // STAAH uses Basic Auth: Base64(ApiKey:ApiSecret) or a bearer token depending on tier.
            // Actual auth scheme to be confirmed with STAAH partner team.
            var request = new HttpRequestMessage(HttpMethod.Post, $"{StaahBaseUrl}/ari/update")
            {
                Content = content
            };
            request.Headers.TryAddWithoutValidation("Authorization",
                $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(connection.EncryptedApiKey))}");

            using var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "STAAH ARI push failed for property {PropertyId}: HTTP {StatusCode} — {Body}",
                    connection.ProviderPropertyId, (int)response.StatusCode, body[..Math.Min(500, body.Length)]);
                return false;
            }

            _logger.LogInformation(
                "STAAH ARI push succeeded for property {PropertyId}: {Count} updates across {Dates} date(s)",
                connection.ProviderPropertyId, updates.Count,
                updates.Select(u => u.Date).Distinct().Count());
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogError(ex,
                "STAAH ARI push transport error for property {PropertyId}", connection.ProviderPropertyId);
            return false;
        }
    }

    // ── Inbound: parse OTA_ResNotifRQ ────────────────────────────────────────

    public Task<InboundBookingDto?> ParseInboundBookingAsync(
        string rawPayload,
        CancellationToken ct = default)
    {
        try
        {
            var doc = XDocument.Parse(rawPayload);
            var root = doc.Root;
            if (root is null) return Task.FromResult<InboundBookingDto?>(null);

            // Support both namespaced and non-namespaced payloads
            XNamespace ns = root.Name.Namespace;

            // Booking reference — first UniqueID at root level, then inside HotelReservation
            var bookingRef = ResolveBookingReference(root, ns);
            if (string.IsNullOrWhiteSpace(bookingRef))
                return Task.FromResult<InboundBookingDto?>(null);

            // OTA source from POS/Source/RequestorID
            var requestorId = root
                .Descendants(ns + "RequestorID")
                .FirstOrDefault()
                ?.Attribute("ID")?.Value ?? "Unknown";
            var otaSource = MapOtaSource(requestorId);

            // HotelReservation element
            var hotelRes = root
                .Descendants(ns + "HotelReservation")
                .FirstOrDefault();
            if (hotelRes is null) return Task.FromResult<InboundBookingDto?>(null);

            // ResGlobalInfo
            var resGlobal = hotelRes.Element(ns + "ResGlobalInfo");

            // Check-in / check-out dates
            var timeSpan = resGlobal?.Element(ns + "TimeSpan");
            if (!DateOnly.TryParse(timeSpan?.Attribute("Start")?.Value, out var checkIn) ||
                !DateOnly.TryParse(timeSpan?.Attribute("End")?.Value, out var checkOut))
                return Task.FromResult<InboundBookingDto?>(null);

            // Guest counts
            var guestCounts = resGlobal?.Elements(ns + "GuestCounts")
                .SelectMany(gc => gc.Elements(ns + "GuestCount")).ToList() ?? [];
            var adults = guestCounts
                .Where(gc => gc.Attribute("AgeQualifyingCode")?.Value == AdultCode)
                .Sum(gc => int.TryParse(gc.Attribute("Count")?.Value, out var n) ? n : 0);
            var children = guestCounts
                .Where(gc => gc.Attribute("AgeQualifyingCode")?.Value == ChildCode)
                .Sum(gc => int.TryParse(gc.Attribute("Count")?.Value, out var n) ? n : 0);
            if (adults == 0) adults = 1; // default to 1 adult if not specified

            // Total amount
            var totalEl = resGlobal?.Element(ns + "Total");
            decimal.TryParse(totalEl?.Attribute("AmountAfterTax")?.Value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var totalAmount);
            var currency = totalEl?.Attribute("CurrencyCode")?.Value ?? "THB";

            // Special requests
            var specialRequests = string.Join("; ",
                resGlobal?.Descendants(ns + "SpecialRequest")
                    .Select(sr => sr.Value.Trim())
                    .Where(v => !string.IsNullOrEmpty(v)) ?? []);

            // Room type code and rate plan code from first RoomStay
            var roomStay = hotelRes
                .Descendants(ns + "RoomStay")
                .FirstOrDefault();
            var channelRoomTypeCode = roomStay
                ?.Descendants(ns + "RoomType")
                .FirstOrDefault()
                ?.Attribute("RoomTypeCode")?.Value ?? string.Empty;
            var channelRatePlanCode = roomStay
                ?.Descendants(ns + "RatePlan")
                .FirstOrDefault()
                ?.Attribute("RatePlanCode")?.Value ?? string.Empty;

            // Guest personal data
            var customer = hotelRes
                .Descendants(ns + "Customer")
                .FirstOrDefault();
            var givenName = customer?.Descendants(ns + "GivenName").FirstOrDefault()?.Value ?? string.Empty;
            var surname = customer?.Descendants(ns + "Surname").FirstOrDefault()?.Value ?? string.Empty;
            var guestFullName = string.Join(" ", new[] { givenName, surname }.Where(s => !string.IsNullOrEmpty(s)));
            var guestEmail = customer?.Descendants(ns + "Email").FirstOrDefault()?.Value;
            var guestPhone = customer?.Descendants(ns + "Telephone")
                .FirstOrDefault()?.Attribute("PhoneNumber")?.Value;

            return Task.FromResult<InboundBookingDto?>(new InboundBookingDto(
                ChannelBookingReference: bookingRef,
                OtaSource: otaSource,
                GuestFullName: string.IsNullOrEmpty(guestFullName) ? "Guest" : guestFullName,
                GuestEmail: NullIfEmpty(guestEmail),
                GuestPhone: NullIfEmpty(guestPhone),
                ChannelRoomTypeCode: channelRoomTypeCode,
                ChannelRatePlanCode: channelRatePlanCode,
                CheckInDate: checkIn,
                CheckOutDate: checkOut,
                Adults: adults,
                Children: children,
                TotalAmount: totalAmount,
                Currency: currency,
                SpecialRequests: NullIfEmpty(specialRequests)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse STAAH OTA_ResNotifRQ payload");
            return Task.FromResult<InboundBookingDto?>(null);
        }
    }

    // ── Webhook signature ────────────────────────────────────────────────────

    public bool ValidateWebhookSignature(
        string rawPayload,
        string? signatureHeader,
        string webhookSecret)
    {
        if (string.IsNullOrEmpty(signatureHeader)) return false;
        var expected = ComputeHmac(rawPayload, webhookSecret);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(signatureHeader));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ResolveBookingReference(XElement root, XNamespace ns)
    {
        // 1. Root-level <UniqueID Type="14" ID="..."/>
        var rootRef = root.Elements(ns + "UniqueID")
            .FirstOrDefault(e => e.Attribute("Type")?.Value == "14")
            ?.Attribute("ID")?.Value;
        if (!string.IsNullOrEmpty(rootRef)) return rootRef.Trim();

        // 2. HotelReservation/<UniqueID Type="14" ID="..."/>
        var resRef = root
            .Descendants(ns + "HotelReservation")
            .FirstOrDefault()
            ?.Elements(ns + "UniqueID")
            .FirstOrDefault(e => e.Attribute("Type")?.Value == "14")
            ?.Attribute("ID")?.Value;
        if (!string.IsNullOrEmpty(resRef)) return resRef.Trim();

        // 3. HotelReservationID ResID_Value
        var resIdRef = root
            .Descendants(ns + "HotelReservationID")
            .FirstOrDefault(e => e.Attribute("ResID_Type")?.Value == "14")
            ?.Attribute("ResID_Value")?.Value;
        return resIdRef?.Trim() ?? string.Empty;
    }

    private static OtaSource MapOtaSource(string requestorId) =>
        requestorId.ToUpperInvariant() switch
        {
            "AGODA" => OtaSource.Agoda,
            "BOOKING" or "BOOKING.COM" or "BOOKINGCOM" => OtaSource.BookingCom,
            "TRAVELOKA" => OtaSource.Traveloka,
            "TRIP" or "TRIP.COM" or "CTRIP" or "TRIPCOM" => OtaSource.TripCom,
            "EXPEDIA" => OtaSource.Expedia,
            "AIRBNB" => OtaSource.Airbnb,
            _ => OtaSource.Unknown
        };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string BuildAvailNotifXml(
        string providerPropertyId,
        IReadOnlyList<AvailabilityUpdate> updates)
    {
        var ns = (XNamespace)"http://www.opentravel.org/OTA/2003/05";
        var rooms = updates
            .GroupBy(u => (u.ChannelRoomTypeCode, u.ChannelRatePlanCode))
            .Select(group =>
                new XElement(ns + "AvailStatusMessage",
                    new XAttribute("BookingLimit", group.Min(u => u.AvailableCount)),
                    new XElement(ns + "StatusApplicationControl",
                        new XAttribute("Start", group.Min(u => u.Date).ToString("yyyy-MM-dd")),
                        new XAttribute("End", group.Max(u => u.Date).ToString("yyyy-MM-dd")),
                        new XAttribute("InvTypeCode", group.Key.ChannelRoomTypeCode),
                        new XAttribute("RatePlanCode", group.Key.ChannelRatePlanCode)
                    )
                )
            );

        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement(ns + "OTA_HotelAvailNotifRQ",
                new XAttribute("Version", "1.0"),
                new XAttribute("HotelCode", providerPropertyId),
                new XElement(ns + "AvailStatusMessages",
                    new XAttribute("HotelCode", providerPropertyId),
                    rooms
                )
            )
        );

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    private static string ComputeHmac(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }
}

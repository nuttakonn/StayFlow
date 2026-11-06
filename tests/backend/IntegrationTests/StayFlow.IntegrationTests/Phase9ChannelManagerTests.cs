using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 9 — Channel Manager integration tests.
///
/// Covers:
///   A. Channel connection CRUD and cross-tenant isolation
///   B. Rate mapping CRUD
///   C. Inbound webhook: signature validation, idempotency, missing mapping, end-to-end booking
///   D. Channel reservation log and cross-tenant isolation
///   E. Manual availability sync trigger
///
/// Uses InMemory database tier — covers application-level rules, tenant scope,
/// and webhook business logic. STAAH/SiteMinder HTTP calls are exercised via
/// the registered provider implementations (which will log transport errors
/// until real credentials are configured; that is expected and not tested here).
/// </summary>
public class Phase9ChannelManagerTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    // Minimal OTA_ResNotifRQ XML matching the STAAH / OTA standard.
    // ChannelRoomTypeCode = "DLX", ChannelRatePlanCode = "BAR", OtaSource = Agoda.
    // Booking reference = "OTA-AGODA-TEST-001".
    private const string StaahBookingXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <OTA_ResNotifRQ xmlns="http://www.opentravel.org/OTA/2003/05" Version="1.0" ResStatus="Commit">
          <UniqueID Type="14" ID="OTA-AGODA-TEST-001"/>
          <POS>
            <Source>
              <RequestorID Type="18" ID="AGODA"/>
            </Source>
          </POS>
          <HotelReservations>
            <HotelReservation>
              <UniqueID Type="14" ID="OTA-AGODA-TEST-001"/>
              <ResGuests>
                <ResGuest>
                  <Profiles>
                    <ProfileInfo>
                      <Profile>
                        <Customer>
                          <PersonName>
                            <GivenName>Somchai</GivenName>
                            <Surname>Jaidee</Surname>
                          </PersonName>
                          <Email>somchai.jaidee@example.com</Email>
                          <Telephone PhoneNumber="+66812345678"/>
                        </Customer>
                      </Profile>
                    </ProfileInfo>
                  </Profiles>
                </ResGuest>
              </ResGuests>
              <ResGlobalInfo>
                <TimeSpan Start="2027-06-10" End="2027-06-13"/>
                <GuestCounts>
                  <GuestCount AgeQualifyingCode="10" Count="2"/>
                  <GuestCount AgeQualifyingCode="8" Count="0"/>
                </GuestCounts>
                <Total AmountAfterTax="5400.00" CurrencyCode="THB"/>
              </ResGlobalInfo>
              <RoomStays>
                <RoomStay>
                  <RoomTypes>
                    <RoomType RoomTypeCode="DLX"/>
                  </RoomTypes>
                  <RatePlans>
                    <RatePlan RatePlanCode="BAR"/>
                  </RatePlans>
                </RoomStay>
              </RoomStays>
            </HotelReservation>
          </HotelReservations>
        </OTA_ResNotifRQ>
        """;

    // A second booking reference — used for conflict tests
    private const string StaahBookingXmlAlt = """
        <?xml version="1.0" encoding="UTF-8"?>
        <OTA_ResNotifRQ xmlns="http://www.opentravel.org/OTA/2003/05" Version="1.0" ResStatus="Commit">
          <UniqueID Type="14" ID="OTA-AGODA-TEST-002"/>
          <POS><Source><RequestorID Type="18" ID="AGODA"/></Source></POS>
          <HotelReservations>
            <HotelReservation>
              <UniqueID Type="14" ID="OTA-AGODA-TEST-002"/>
              <ResGuests>
                <ResGuest>
                  <Profiles>
                    <ProfileInfo>
                      <Profile>
                        <Customer>
                          <PersonName><GivenName>Napat</GivenName><Surname>Srisuk</Surname></PersonName>
                          <Email>napat.srisuk@example.com</Email>
                        </Customer>
                      </Profile>
                    </ProfileInfo>
                  </Profiles>
                </ResGuest>
              </ResGuests>
              <ResGlobalInfo>
                <TimeSpan Start="2027-06-11" End="2027-06-14"/>
                <GuestCounts>
                  <GuestCount AgeQualifyingCode="10" Count="1"/>
                </GuestCounts>
                <Total AmountAfterTax="5400.00" CurrencyCode="THB"/>
              </ResGlobalInfo>
              <RoomStays>
                <RoomStay>
                  <RoomTypes><RoomType RoomTypeCode="DLX"/></RoomTypes>
                  <RatePlans><RatePlan RatePlanCode="BAR"/></RatePlans>
                </RoomStay>
              </RoomStays>
            </HotelReservation>
          </HotelReservations>
        </OTA_ResNotifRQ>
        """;

    public Phase9ChannelManagerTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p9")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase9 User",
            PhoneNumber: "+66800009999",
            OrganizationName: $"P9 Org {Guid.NewGuid():N}"[..20]
        );
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return (auth!.AccessToken, auth.ActiveOrganizationId!.Value);
    }

    private HttpRequestMessage Auth(HttpMethod method, string url, string token, Guid orgId, object? body = null)
    {
        var msg = new HttpRequestMessage(method, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        if (body != null) msg.Content = JsonContent.Create(body);
        return msg;
    }

    private async Task<Guid> CreatePropertyAsync(string token, Guid orgId)
    {
        var req = new CreatePropertyRequest(
            Name: "P9 Hotel", Code: $"P9{Guid.NewGuid():N}"[..6],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd, Bangkok",
            ContactPhone: "+66800001111", ContactEmail: "p9@test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var prop = await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);
        return prop!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propertyId, string code = "DLX9")
    {
        var req = new CreateRoomTypeRequest(
            Name: "Deluxe P9", Code: code,
            BaseRate: 1800m, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var rt = await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);
        return rt!.Id;
    }

    private async Task<Guid> CreateRoomAsync(string token, Guid orgId, Guid propertyId, Guid roomTypeId)
    {
        var req = new CreateRoomRequest(
            RoomTypeId: roomTypeId,
            RoomNumber: $"R9-{Guid.NewGuid():N}"[..8],
            BuildingId: null, FloorId: null, DisplayName: null,
            Capacity: 2, OperationalStatus: RoomOperationalStatus.Clean, Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var room = await res.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        return room!.Id;
    }

    // Create a STAAH connection; returns (connectionId, apiKey used as webhook secret)
    private async Task<(Guid connectionId, string apiKey)> CreateConnectionAsync(
        string token, Guid orgId, Guid propertyId, int provider = 1)
    {
        var apiKey = $"test-secret-{Guid.NewGuid():N}";
        var req = new CreateChannelConnectionRequest(
            Provider: provider,
            ProviderPropertyId: "STAAH-HOTEL-123",
            ApiKey: apiKey,
            ApiSecret: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var conn = await res.Content.ReadFromJsonAsync<ChannelConnectionDto>(_jsonOptions);
        return (conn!.Id, apiKey);
    }

    private async Task<Guid> CreateRateMappingAsync(
        string token, Guid orgId, Guid propertyId, Guid connectionId,
        Guid roomTypeId, string channelRoomTypeCode = "DLX", string channelRatePlanCode = "BAR")
    {
        var req = new CreateRateMappingRequest(
            RoomTypeId: roomTypeId,
            ChannelRoomTypeCode: channelRoomTypeCode,
            ChannelRatePlanCode: channelRatePlanCode);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propertyId}/channel-connections/{connectionId}/rate-mappings",
            token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var mapping = await res.Content.ReadFromJsonAsync<ChannelRateMappingDto>(_jsonOptions);
        return mapping!.Id;
    }

    private static string ComputeHmac(string payload, string secret)
    {
        var key = Encoding.UTF8.GetBytes(secret);
        var data = Encoding.UTF8.GetBytes(payload);
        return Convert.ToHexString(HMACSHA256.HashData(key, data)).ToLowerInvariant();
    }

    private HttpRequestMessage WebhookRequest(Guid connectionId, string payload, string? signature)
    {
        var msg = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/channel/{connectionId}");
        msg.Content = new StringContent(payload, Encoding.UTF8, "application/xml");
        if (signature != null)
            msg.Headers.Add("X-Channel-Signature", signature);
        return msg;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A. Channel Connection CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateConnection_WithValidData_Returns201()
    {
        var (token, orgId) = await RegisterAsync("conn_create");
        var propertyId = await CreatePropertyAsync(token, orgId);

        var req = new CreateChannelConnectionRequest(1, "STAAH-999", "my-api-key", "my-api-secret");
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ChannelConnectionDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(propertyId, dto!.PropertyId);
        Assert.Equal(1, dto.Provider);
        Assert.Equal("STAAH-999", dto.ProviderPropertyId);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task CreateConnection_ApiKeyNotReturnedInResponse()
    {
        // The API key / secret must never be returned in any response body.
        var (token, orgId) = await RegisterAsync("conn_key");
        var propertyId = await CreatePropertyAsync(token, orgId);

        var req = new CreateChannelConnectionRequest(1, "STAAH-888", "super-secret-key", null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("super-secret-key", body);
    }

    [Fact]
    public async Task CreateConnection_DuplicateProviderForSameProperty_Returns409()
    {
        var (token, orgId) = await RegisterAsync("conn_dup");
        var propertyId = await CreatePropertyAsync(token, orgId);

        await CreateConnectionAsync(token, orgId, propertyId, provider: 1);

        // Second STAAH connection for the same property
        var req = new CreateChannelConnectionRequest(1, "STAAH-ANOTHER", "another-key", null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId, req));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task ListConnections_ReturnsCreatedConnection()
    {
        var (token, orgId) = await RegisterAsync("conn_list");
        var propertyId = await CreatePropertyAsync(token, orgId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propertyId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<List<ChannelConnectionDto>>(_jsonOptions);
        Assert.NotNull(list);
        Assert.Contains(list!, c => c.Id == connId);
    }

    [Fact]
    public async Task DeleteConnection_DeactivatesAndDisappearsFromActiveList()
    {
        var (token, orgId) = await RegisterAsync("conn_del");
        var propertyId = await CreatePropertyAsync(token, orgId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propertyId);

        var delRes = await _client.SendAsync(Auth(HttpMethod.Delete,
            $"/api/v1/properties/{propertyId}/channel-connections/{connId}", token, orgId));
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Deleted (deactivated) connection should no longer appear in list
        var listRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propertyId}/channel-connections", token, orgId));
        var list = await listRes.Content.ReadFromJsonAsync<List<ChannelConnectionDto>>(_jsonOptions);
        Assert.DoesNotContain(list!, c => c.Id == connId);
    }

    [Fact]
    public async Task CrossTenant_OrgACannotListOrgBConnections()
    {
        var (tokenA, orgAId) = await RegisterAsync("ch_xta");
        var (tokenB, orgBId) = await RegisterAsync("ch_xtb");

        var propB = await CreatePropertyAsync(tokenB, orgBId);
        await CreateConnectionAsync(tokenB, orgBId, propB);

        // Org A uses Org B's propertyId but Org A's auth context
        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/channel-connections", tokenA, orgAId));

        // Must not return Org B's data — expect empty list or 403/404
        if (res.StatusCode == HttpStatusCode.OK)
        {
            var list = await res.Content.ReadFromJsonAsync<List<ChannelConnectionDto>>(_jsonOptions);
            Assert.Empty(list!);
        }
        else
        {
            Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"Expected empty/403/404, got {res.StatusCode}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B. Rate Mapping CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRateMapping_WithValidData_Returns201()
    {
        var (token, orgId) = await RegisterAsync("map_create");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"RT{Guid.NewGuid():N}"[..6]);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);

        var req = new CreateRateMappingRequest(rtId, "DLXCHANNEL", "BARCHANNEL");
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/rate-mappings", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ChannelRateMappingDto>(_jsonOptions);
        Assert.Equal(connId, dto!.ChannelConnectionId);
        Assert.Equal(rtId, dto.RoomTypeId);
        Assert.Equal("DLXCHANNEL", dto.ChannelRoomTypeCode);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task CreateRateMapping_DuplicateRoomType_Returns409()
    {
        var (token, orgId) = await RegisterAsync("map_dup");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"MD{Guid.NewGuid():N}"[..6]);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);

        await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        // Second mapping for same room type
        var req = new CreateRateMappingRequest(rtId, "DLX", "BAR2");
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/rate-mappings", token, orgId, req));

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task ListRateMappings_ReturnsActiveMapping()
    {
        var (token, orgId) = await RegisterAsync("map_list");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"ML{Guid.NewGuid():N}"[..6]);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);
        var mappingId = await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/rate-mappings", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<List<ChannelRateMappingDto>>(_jsonOptions);
        Assert.Contains(list!, m => m.Id == mappingId);
    }

    [Fact]
    public async Task DeleteRateMapping_RemovesFromActiveList()
    {
        var (token, orgId) = await RegisterAsync("map_del");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"MD2{Guid.NewGuid():N}"[..6]);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);
        var mappingId = await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var delRes = await _client.SendAsync(Auth(HttpMethod.Delete,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/rate-mappings/{mappingId}", token, orgId));
        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        var listRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/rate-mappings", token, orgId));
        var list = await listRes.Content.ReadFromJsonAsync<List<ChannelRateMappingDto>>(_jsonOptions);
        Assert.DoesNotContain(list!, m => m.Id == mappingId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C. Inbound Webhook
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Webhook_NonexistentConnection_Returns404()
    {
        var payload = StaahBookingXml;
        var sig = ComputeHmac(payload, "any-secret");

        var res = await _client.SendAsync(WebhookRequest(Guid.NewGuid(), payload, sig));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Webhook_MissingSignatureHeader_Returns401()
    {
        var (token, orgId) = await RegisterAsync("wh_nosig");
        var propId = await CreatePropertyAsync(token, orgId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);

        // No X-Channel-Signature header
        var res = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, signature: null));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Webhook_WrongSignature_Returns401()
    {
        var (token, orgId) = await RegisterAsync("wh_badsig");
        var propId = await CreatePropertyAsync(token, orgId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);

        // Signature computed with the wrong secret
        var sig = ComputeHmac(StaahBookingXml, "not-the-real-secret");
        var res = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Webhook_ValidSignature_NoRateMapping_Returns409AndLogsFailedRecord()
    {
        var (token, orgId) = await RegisterAsync("wh_nomap");
        var propId = await CreatePropertyAsync(token, orgId);
        // Connection exists but no rate mapping
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);

        var sig = ComputeHmac(StaahBookingXml, apiKey);
        var res = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));

        // Channel reservation should be created with Failed status
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);

        // The failed ChannelReservation should appear in the log
        var logRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/channel-reservations", token, orgId));
        Assert.Equal(HttpStatusCode.OK, logRes.StatusCode);
        var log = await logRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var total = log.GetProperty("total").GetInt32();
        Assert.True(total >= 1, "Failed ChannelReservation should appear in the log");
    }

    [Fact]
    public async Task Webhook_ValidPayload_CreatesReservationAndChannelReservationRecord()
    {
        // Full end-to-end: STAAH XML → parsed → reservation created in PMS.
        var (token, orgId) = await RegisterAsync("wh_ok");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"WH{Guid.NewGuid():N}"[..6]);
        // A room must exist for availability to be > 0
        await CreateRoomAsync(token, orgId, propId, rtId);
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);
        // Rate mapping with ChannelRoomTypeCode = "DLX" to match the XML payload
        await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var sig = ComputeHmac(StaahBookingXml, apiKey);
        var res = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(body.TryGetProperty("reservationId", out _),
            "Response must contain reservationId after successful booking creation");
        Assert.True(body.TryGetProperty("channelReservationId", out _));
    }

    [Fact]
    public async Task Webhook_DuplicateBookingReference_Returns200Idempotent()
    {
        // The same webhook delivered twice must not create a second reservation.
        var (token, orgId) = await RegisterAsync("wh_idem");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"WI{Guid.NewGuid():N}"[..6]);
        await CreateRoomAsync(token, orgId, propId, rtId);
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);
        await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var sig = ComputeHmac(StaahBookingXml, apiKey);

        // First delivery — should succeed
        var first = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second delivery of the identical payload
        var second = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        body.TryGetProperty("message", out var msg);
        Assert.Contains("Duplicate", msg.GetString() ?? "", StringComparison.OrdinalIgnoreCase);

        // Only one reservation should exist for this channel reference
        var logRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/channel-reservations", token, orgId));
        var log = await logRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var items = log.GetProperty("data").EnumerateArray()
            .Where(e => e.GetProperty("channelBookingReference").GetString() == "OTA-AGODA-TEST-001")
            .ToList();
        Assert.Single(items);
    }

    [Fact]
    public async Task Webhook_RoomUnavailableDueToConflict_Returns409()
    {
        // Room is available. First OTA booking takes the last room.
        // A second OTA booking for overlapping dates must be rejected with 409.
        var (token, orgId) = await RegisterAsync("wh_conf");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"WC{Guid.NewGuid():N}"[..6]);
        // Only one room — so second booking for same dates will conflict
        await CreateRoomAsync(token, orgId, propId, rtId);
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);
        await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var sig1 = ComputeHmac(StaahBookingXml, apiKey);
        var first = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig1));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second booking overlaps same dates (2027-06-11 to 2027-06-14 overlaps 2027-06-10 to 2027-06-13)
        var sig2 = ComputeHmac(StaahBookingXmlAlt, apiKey);
        var second = await _client.SendAsync(WebhookRequest(connId, StaahBookingXmlAlt, sig2));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Webhook_InactiveConnection_Returns404()
    {
        var (token, orgId) = await RegisterAsync("wh_inactive");
        var propId = await CreatePropertyAsync(token, orgId);
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);

        // Deactivate the connection
        await _client.SendAsync(Auth(HttpMethod.Delete,
            $"/api/v1/properties/{propId}/channel-connections/{connId}", token, orgId));

        var sig = ComputeHmac(StaahBookingXml, apiKey);
        var res = await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // D. Channel reservation log
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListChannelReservations_ReturnsLog_WithPagination()
    {
        var (token, orgId) = await RegisterAsync("chlog");
        var propId = await CreatePropertyAsync(token, orgId);
        var (connId, apiKey) = await CreateConnectionAsync(token, orgId, propId);

        // Trigger a failed booking (no rate mapping) so the log has at least one entry
        var sig = ComputeHmac(StaahBookingXml, apiKey);
        await _client.SendAsync(WebhookRequest(connId, StaahBookingXml, sig));

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/channel-reservations?pageSize=20&page=1", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(body.TryGetProperty("data", out _));
        Assert.True(body.TryGetProperty("total", out _));
        Assert.True(body.TryGetProperty("page", out _));
    }

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBChannelReservations()
    {
        var (tokenA, orgAId) = await RegisterAsync("chxt_a");
        var (tokenB, orgBId) = await RegisterAsync("chxt_b");

        var propB = await CreatePropertyAsync(tokenB, orgBId);
        var (connIdB, apiKeyB) = await CreateConnectionAsync(tokenB, orgBId, propB);

        // Org B receives a webhook (creates a ChannelReservation, even if failed)
        var sig = ComputeHmac(StaahBookingXml, apiKeyB);
        await _client.SendAsync(WebhookRequest(connIdB, StaahBookingXml, sig));

        // Org A tries to read Org B's channel reservation log
        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/channel-reservations", tokenA, orgAId));

        if (res.StatusCode == HttpStatusCode.OK)
        {
            var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
            var total = body.GetProperty("total").GetInt32();
            Assert.Equal(0, total);
        }
        else
        {
            Assert.True(res.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
                $"Expected empty list / 403 / 404, got {res.StatusCode}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // E. Manual availability sync trigger
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ManualSync_WithValidConnectionAndDates_Returns200()
    {
        var (token, orgId) = await RegisterAsync("sync_ok");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, $"SY{Guid.NewGuid():N}"[..6]);
        await CreateRoomAsync(token, orgId, propId, rtId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);
        await CreateRateMappingAsync(token, orgId, propId, connId, rtId, "DLX", "BAR");

        var req = new { fromDate = "2027-06-01", toDate = "2027-06-30" };
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/sync", token, orgId, req));

        // Channel manager transport will log errors (no real STAAH creds in tests)
        // but the PMS operation must succeed
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task ManualSync_WithInvalidDates_Returns400()
    {
        var (token, orgId) = await RegisterAsync("sync_bad");
        var propId = await CreatePropertyAsync(token, orgId);
        var (connId, _) = await CreateConnectionAsync(token, orgId, propId);

        // toDate before fromDate
        var req = new { fromDate = "2027-06-30", toDate = "2027-06-01" };
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/channel-connections/{connId}/sync", token, orgId, req));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task ManualSync_NonexistentConnection_Returns404()
    {
        var (token, orgId) = await RegisterAsync("sync_miss");
        var propId = await CreatePropertyAsync(token, orgId);

        var req = new { fromDate = "2027-06-01", toDate = "2027-06-30" };
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/channel-connections/{Guid.NewGuid()}/sync", token, orgId, req));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

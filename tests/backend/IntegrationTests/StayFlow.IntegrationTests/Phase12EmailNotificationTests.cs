using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 12 — Email automation (booking confirmation, daily report).
///
/// Covers:
///   A. Email settings: defaults returned when no record exists
///   B. Email settings: create/update per property
///   C. Email settings: validation (hour 0–23)
///   D. Email settings: cross-property isolation
///   E. Booking confirmation: NullEmailService captures message on confirm
///   F. Booking confirmation: not sent when guest has no email
///   G. Booking confirmation: SendBookingConfirmation=false suppresses send
///   H. Daily report manual trigger: returns 200
///   I. Daily report manual trigger: returns 404 for wrong property
/// </summary>
public class Phase12EmailNotificationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase12EmailNotificationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p12")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase12 User",
            PhoneNumber: "+66800001212",
            OrganizationName: $"P12 Org {Guid.NewGuid():N}"[..20]);
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
            Name: "P12 Hotel", Code: $"P12{Guid.NewGuid():N}"[..5],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd", ContactPhone: "+6680000",
            ContactEmail: "support@p12test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propId)
    {
        var req = new CreateRoomTypeRequest(
            Name: "Standard P12", Code: $"S12{Guid.NewGuid():N}"[..5],
            BaseRate: 1500m, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId, string? email = null)
    {
        var req = new CreateGuestRequest(
            FullName: "P12 Guest",
            Email: email ?? $"p12_{Guid.NewGuid():N}@g.com",
            Phone: null, NationalId: null, PassportNumber: null,
            Nationality: null, Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateAndConfirmReservationAsync(
        string token, Guid orgId, Guid propId, Guid rtId, Guid guestId)
    {
        var createReq = new CreateReservationRequest(
            PropertyId: propId, GuestId: guestId,
            CheckInDate: "2027-11-10", CheckOutDate: "2027-11-13",
            Adults: 2, Children: 0, RoomTypeId: rtId, RoomId: null,
            QuotedRate: 1500m, TotalAmount: 4500m, Currency: "THB",
            Source: "Phone", SpecialRequests: null, Notes: null);
        var createRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createReq));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var resId = (await createRes.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions))!.Id;

        var confirmRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/reservations/{resId}/confirm", token, orgId));
        Assert.Equal(HttpStatusCode.OK, confirmRes.StatusCode);
        return resId;
    }

    // ── A. Email settings defaults ────────────────────────────────────────────

    [Fact]
    public async Task GetEmailSettings_NoRecord_ReturnsDefaults()
    {
        var (token, orgId) = await RegisterAsync("p12_defaults");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/email-settings", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<EmailSettingsDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.True(dto.SendBookingConfirmation);  // default true
        Assert.False(dto.SendDailyReport);         // default false
        Assert.Equal("", dto.DailyReportRecipients);
        Assert.Equal(6, dto.DailyReportSendHourUtc);
    }

    // ── B. Email settings create/update ───────────────────────────────────────

    [Fact]
    public async Task UpdateEmailSettings_CreatesThenUpdates()
    {
        var (token, orgId) = await RegisterAsync("p12_update");
        var propId = await CreatePropertyAsync(token, orgId);

        var req = new UpdateEmailSettingsRequest(
            SendBookingConfirmation: true,
            SendDailyReport: true,
            DailyReportRecipients: "mgr@hotel.com, gm@hotel.com",
            DailyReportSendHourUtc: 7);

        var res = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/email-settings", token, orgId, req));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<EmailSettingsDto>(_jsonOptions);
        Assert.True(dto!.SendDailyReport);
        Assert.Equal("mgr@hotel.com, gm@hotel.com", dto.DailyReportRecipients);
        Assert.Equal(7, dto.DailyReportSendHourUtc);

        // Update again: round-trips correctly
        var update2 = new UpdateEmailSettingsRequest(false, false, "", 6);
        var res2 = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/email-settings", token, orgId, update2));
        var dto2 = await res2.Content.ReadFromJsonAsync<EmailSettingsDto>(_jsonOptions);
        Assert.False(dto2!.SendBookingConfirmation);
        Assert.False(dto2.SendDailyReport);
    }

    // ── C. Validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateEmailSettings_InvalidHour_Returns400()
    {
        var (token, orgId) = await RegisterAsync("p12_valid");
        var propId = await CreatePropertyAsync(token, orgId);

        var req = new UpdateEmailSettingsRequest(true, false, "", 25); // invalid hour

        var res = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/email-settings", token, orgId, req));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── D. Cross-property isolation ───────────────────────────────────────────

    [Fact]
    public async Task EmailSettings_AreIsolatedAcrossOrgs()
    {
        var (tokenA, orgA) = await RegisterAsync("p12_isoA");
        var (tokenB, orgB) = await RegisterAsync("p12_isoB");
        var propA = await CreatePropertyAsync(tokenA, orgA);
        var propB = await CreatePropertyAsync(tokenB, orgB);

        // Set up settings for A
        await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propA}/email-settings", tokenA, orgA,
            new UpdateEmailSettingsRequest(false, true, "a@hotel.com", 5)));

        // Org B should still see defaults for their property
        var resB = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/email-settings", tokenB, orgB));
        var dtoB = await resB.Content.ReadFromJsonAsync<EmailSettingsDto>(_jsonOptions);
        Assert.True(dtoB!.SendBookingConfirmation); // default
        Assert.False(dtoB.SendDailyReport);         // default

        // Org B cannot read Org A's property settings
        var crossRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propA}/email-settings", tokenB, orgB));
        Assert.Equal(HttpStatusCode.NotFound, crossRes.StatusCode);
    }

    // ── E. Booking confirmation captured by NullEmailService ─────────────────

    [Fact]
    public async Task ConfirmReservation_WithGuestEmail_CapturesConfirmationEmail()
    {
        var (token, orgId) = await RegisterAsync("p12_confirm");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, email: "guest_p12@example.com");

        var nullEmail = _factory.GetNullEmailService();
        var countBefore = nullEmail?.SentMessages.Count ?? 0;

        await CreateAndConfirmReservationAsync(token, orgId, propId, rtId, guestId);

        var countAfter = nullEmail?.SentMessages.Count ?? 0;
        Assert.True(countAfter > countBefore, "Expected at least one email to be captured");

        var last = nullEmail!.SentMessages.Last();
        Assert.Equal("guest_p12@example.com", last.To);
        Assert.Contains("Confirmed", last.Subject, StringComparison.OrdinalIgnoreCase);
    }

    // ── F. No email when guest has no email address ───────────────────────────

    [Fact]
    public async Task ConfirmReservation_GuestWithoutEmail_NoEmailSent()
    {
        var (token, orgId) = await RegisterAsync("p12_noemail");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);

        // Guest without email
        var req = new CreateGuestRequest("No Email Guest", null, null, null, null, null, null);
        var gRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        var guestId = (await gRes.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions))!.Id;

        var nullEmail = _factory.GetNullEmailService();
        var countBefore = nullEmail?.SentMessages.Count ?? 0;

        await CreateAndConfirmReservationAsync(token, orgId, propId, rtId, guestId);

        var countAfter = nullEmail?.SentMessages.Count ?? 0;
        Assert.Equal(countBefore, countAfter);
    }

    // ── G. SendBookingConfirmation=false suppresses send ─────────────────────

    [Fact]
    public async Task ConfirmReservation_ConfirmationDisabled_NoEmailSent()
    {
        var (token, orgId) = await RegisterAsync("p12_disabled");
        var propId = await CreatePropertyAsync(token, orgId);

        // Disable confirmation
        await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/email-settings", token, orgId,
            new UpdateEmailSettingsRequest(false, false, "", 6)));

        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, email: "noemail_p12@example.com");

        var nullEmail = _factory.GetNullEmailService();
        var countBefore = nullEmail?.SentMessages.Count ?? 0;

        await CreateAndConfirmReservationAsync(token, orgId, propId, rtId, guestId);

        var countAfter = nullEmail?.SentMessages.Count ?? 0;
        Assert.Equal(countBefore, countAfter);
    }

    // ── H. Daily report manual trigger ───────────────────────────────────────

    [Fact]
    public async Task SendDailyReportNow_ReturnsOk()
    {
        var (token, orgId) = await RegisterAsync("p12_daily");
        var propId = await CreatePropertyAsync(token, orgId);

        // Configure recipients so the report can run
        await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/email-settings", token, orgId,
            new UpdateEmailSettingsRequest(true, true, "daily@hotel.com", 6)));

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/email-settings/send-daily-report?date=2027-11-01",
            token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(body.GetProperty("sent").GetBoolean());
    }

    // ── I. Daily report returns 404 for wrong property ────────────────────────

    [Fact]
    public async Task SendDailyReportNow_WrongProperty_Returns404()
    {
        var (token, orgId) = await RegisterAsync("p12_wrong");
        var randomPropId = Guid.NewGuid();

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{randomPropId}/email-settings/send-daily-report",
            token, orgId));

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}

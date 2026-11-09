using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 11 — Thai government compliance (R.S.3, R.S.4, TM.30).
///
/// Covers:
///   A. Thai compliance mode: disabled by default, enable/disable per property
///   B. Record creation: Thai guests (NationalId), foreign guests (PassportNumber)
///   C. Validation: cannot create record without NationalId or PassportNumber
///   D. Duplicate record guard (one record per reservation)
///   E. Amendment: before/after values audited
///   F. TM.30 returns foreigners only
///   G. R.S.3, R.S.4, TM.30 Excel exports return xlsx content-type
///   H. Guest profile now accepts DateOfBirth, VisaType, CountryOfOrigin
///   I. Cross-property isolation
/// </summary>
public class Phase11ThaiComplianceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase11ThaiComplianceTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p11")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase11 User",
            PhoneNumber: "+66800009999",
            OrganizationName: $"P11 Org {Guid.NewGuid():N}"[..20]);
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
            Name: "P11 Hotel", Code: $"P11{Guid.NewGuid():N}"[..5],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd", ContactPhone: "+6680000",
            ContactEmail: "p11@test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId,
        string? nationalId = null, string? passportNumber = null,
        string? nationality = "Thai", string? dateOfBirth = "1990-05-15",
        string? visaType = null, string? countryOfOrigin = "Thailand")
    {
        var req = new CreateGuestRequest(
            FullName: "P11 Guest", Email: $"p11_{Guid.NewGuid():N}@g.com",
            Phone: null, NationalId: nationalId, PassportNumber: passportNumber,
            Nationality: nationality, Notes: null,
            DateOfBirth: dateOfBirth, VisaType: visaType, CountryOfOrigin: countryOfOrigin);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propId)
    {
        var req = new CreateRoomTypeRequest(
            Name: "Standard P11", Code: $"S11{Guid.NewGuid():N}"[..5],
            BaseRate: 1200m, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateReservationAsync(string token, Guid orgId, Guid propId, Guid rtId, Guid guestId)
    {
        var req = new CreateReservationRequest(
            PropertyId: propId, GuestId: guestId,
            CheckInDate: "2027-09-10", CheckOutDate: "2027-09-13",
            Adults: 2, Children: 0, RoomTypeId: rtId, RoomId: null,
            QuotedRate: 1200m, TotalAmount: 3600m, Currency: "THB", Source: "Phone",
            SpecialRequests: null, Notes: null, RatePlanId: null, CouponCode: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions))!.Id;
    }

    private async Task EnableThaiComplianceAsync(string token, Guid orgId, Guid propId)
    {
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/enable", token, orgId,
            new EnableThaiComplianceRequest(true)));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // ── A. Thai compliance mode ───────────────────────────────────────────────

    [Fact]
    public async Task ThaiCompliance_DisabledByDefault_CreateRecordReturns403()
    {
        var (token, orgId) = await RegisterAsync("tc_disabled");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, nationalId: "1234567890123");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "P11 Guest", "1234567890123", null,
            "Thai", "1990-05-15", null, "Thailand", "101");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task EnableThaiCompliance_AllowsRecordCreation()
    {
        var (token, orgId) = await RegisterAsync("tc_enable");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, nationalId: "1234567890124");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "P11 Guest", "1234567890124", null,
            "Thai", "1990-05-15", null, "Thailand", "102");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ComplianceRecordDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("P11 Guest", dto.GuestFullName);
        Assert.False(dto.IsForeigner);
    }

    // ── B. Thai vs foreign guest ──────────────────────────────────────────────

    [Fact]
    public async Task CreateRecord_ThaiGuest_IsNotForeigner()
    {
        var (token, orgId) = await RegisterAsync("tc_thai");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, nationalId: "1111111111111");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "Thai Person", "1111111111111", null,
            "Thai", "1985-03-20", null, "Thailand", "201");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));
        var dto = await res.Content.ReadFromJsonAsync<ComplianceRecordDto>(_jsonOptions);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.False(dto!.IsForeigner);
        Assert.Equal("1111111111111", dto.NationalId);
        Assert.Null(dto.PassportNumber);
    }

    [Fact]
    public async Task CreateRecord_ForeignGuest_IsForeigner()
    {
        var (token, orgId) = await RegisterAsync("tc_foreign");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, passportNumber: "AB123456",
            nationality: "British", visaType: "Tourist", countryOfOrigin: "United Kingdom");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "John Smith", null, "AB123456",
            "British", "1980-07-04", "Tourist", "United Kingdom", "301");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));
        var dto = await res.Content.ReadFromJsonAsync<ComplianceRecordDto>(_jsonOptions);

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        Assert.True(dto!.IsForeigner);
        Assert.Equal("AB123456", dto.PassportNumber);
        Assert.Equal("Tourist", dto.VisaType);
    }

    // ── C. Validation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecord_NeitherNationalIdNorPassport_Returns400()
    {
        var (token, orgId) = await RegisterAsync("tc_invalid");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "No ID Guest", null, null,
            "Thai", "1990-01-01", null, "Thailand", "401");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── D. Duplicate guard ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRecord_DuplicateForSameReservation_Returns409()
    {
        var (token, orgId) = await RegisterAsync("tc_dup");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, nationalId: "2222222222222");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var req = new CreateComplianceRecordRequest(
            resId, guestId, "Dup Guest", "2222222222222", null,
            "Thai", "1975-11-11", null, "Thailand", "501");

        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));

        var second = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, req));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── E. Amendment ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AmendRecord_UpdatesFieldsAndCreatesAuditTrail()
    {
        var (token, orgId) = await RegisterAsync("tc_amend");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, passportNumber: "CD789012",
            nationality: "French", visaType: "Tourist", countryOfOrigin: "France");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        var createReq = new CreateComplianceRecordRequest(
            resId, guestId, "Pierre Dupont", null, "CD789012",
            "French", "1988-04-15", "Tourist", "France", "601");
        var createRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId, createReq));
        var record = await createRes.Content.ReadFromJsonAsync<ComplianceRecordDto>(_jsonOptions);

        // Amend: passport number was entered incorrectly
        var amendReq = new AmendComplianceRecordRequest(
            Reason: "Passport number correction",
            NewNationalId: null, NewPassportNumber: "CD789099",
            NewNationality: "French", NewDateOfBirth: "1988-04-15",
            NewVisaType: "Tourist", NewCountryOfOrigin: "France");

        var amendRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records/{record!.Id}/amend", token, orgId, amendReq));

        Assert.Equal(HttpStatusCode.OK, amendRes.StatusCode);
        var amended = await amendRes.Content.ReadFromJsonAsync<ComplianceRecordDto>(_jsonOptions);
        Assert.True(amended!.IsAmended);
        Assert.Equal(1, amended.AmendmentCount);
        Assert.Equal("CD789099", amended.PassportNumber);
    }

    // ── F. TM.30 foreigners only ──────────────────────────────────────────────

    [Fact]
    public async Task ListRecords_ForeignersOnlyFilter_ExcludesThaiGuests()
    {
        var (token, orgId) = await RegisterAsync("tc_tm30filter");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);

        // Thai guest
        var thaiGuestId = await CreateGuestAsync(token, orgId, nationalId: "3333333333333");
        var resId1 = await CreateReservationAsync(token, orgId, propId, rtId, thaiGuestId);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId,
            new CreateComplianceRecordRequest(resId1, thaiGuestId, "Thai Guest",
                "3333333333333", null, "Thai", "1990-01-01", null, "Thailand", "701")));

        // Foreign guest — need separate reservation
        var foreignGuestId = await CreateGuestAsync(token, orgId, passportNumber: "EF456789",
            nationality: "German", visaType: "Business", countryOfOrigin: "Germany");
        var resId2 = await CreateReservationAsync(token, orgId, propId, rtId, foreignGuestId);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId,
            new CreateComplianceRecordRequest(resId2, foreignGuestId, "Hans Müller",
                null, "EF456789", "German", "1975-08-22", "Business", "Germany", "702")));

        // All records: 2
        var all = await (await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId)))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(2, all.GetProperty("total").GetInt32());

        // Foreigners only: 1
        var foreignOnly = await (await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/thai-compliance/records?foreignersOnly=true", token, orgId)))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(1, foreignOnly.GetProperty("total").GetInt32());
    }

    // ── G. Excel exports ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExportRs3_ReturnsXlsxWithValidMagicBytes()
    {
        var (token, orgId) = await RegisterAsync("tc_rs3");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/thai-compliance/rs3/export", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
        Assert.Equal(0x50, bytes[0]); // PK zip magic
        Assert.Equal(0x4B, bytes[1]);
    }

    [Fact]
    public async Task ExportRs4_ReturnsXlsx()
    {
        var (token, orgId) = await RegisterAsync("tc_rs4");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/thai-compliance/rs4/export", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ExportTm30_ReturnsXlsx_ForeignGuestsOnly()
    {
        var (token, orgId) = await RegisterAsync("tc_tm30export");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableThaiComplianceAsync(token, orgId, propId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId, passportNumber: "GH111222",
            nationality: "Japanese", visaType: "Tourist", countryOfOrigin: "Japan");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId);

        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/thai-compliance/records", token, orgId,
            new CreateComplianceRecordRequest(resId, guestId, "Tanaka Yuki",
                null, "GH111222", "Japanese", "1992-11-03", "Tourist", "Japan", "801")));

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/thai-compliance/tm30/export", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
    }

    // ── H. Guest profile Thai compliance fields ───────────────────────────────

    [Fact]
    public async Task CreateGuest_WithThaiComplianceFields_StoredCorrectly()
    {
        var (token, orgId) = await RegisterAsync("tc_guestfields");

        var req = new CreateGuestRequest(
            FullName: "Maria Garcia",
            Email: $"maria_{Guid.NewGuid():N}@test.com",
            Phone: null, NationalId: null,
            PassportNumber: "SP999888",
            Nationality: "Spanish",
            Notes: null,
            DateOfBirth: "1995-06-25",
            VisaType: "Tourist",
            CountryOfOrigin: "Spain");

        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        var dto = await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);
        Assert.Equal("1995-06-25", dto!.DateOfBirth);
        Assert.Equal("Tourist", dto.VisaType);
        Assert.Equal("Spain", dto.CountryOfOrigin);
    }

    // ── I. Cross-property isolation ───────────────────────────────────────────

    [Fact]
    public async Task ComplianceRecords_CrossProperty_AreIsolated()
    {
        var (tokenA, orgA) = await RegisterAsync("tc_isoA");
        var (tokenB, orgB) = await RegisterAsync("tc_isoB");
        var propA = await CreatePropertyAsync(tokenA, orgA);
        var propB = await CreatePropertyAsync(tokenB, orgB);

        // Enable compliance on A, create a record
        await EnableThaiComplianceAsync(tokenA, orgA, propA);
        var rtId = await CreateRoomTypeAsync(tokenA, orgA, propA);
        var guestId = await CreateGuestAsync(tokenA, orgA, nationalId: "9999999999999");
        var resId = await CreateReservationAsync(tokenA, orgA, propA, rtId, guestId);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propA}/thai-compliance/records", tokenA, orgA,
            new CreateComplianceRecordRequest(resId, guestId, "Org A Guest",
                "9999999999999", null, "Thai", "1980-01-01", null, "Thailand", "901")));

        // Enable compliance on B — should see 0 records for propB
        await EnableThaiComplianceAsync(tokenB, orgB, propB);
        var listB = await (await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/thai-compliance/records", tokenB, orgB)))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(0, listB.GetProperty("total").GetInt32());
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 10.5 — Ancillary services, meal packages, quotations, ADR/RevPAR, Excel export.
///
/// Covers:
///   A. ServiceItem CRUD, tenant isolation
///   B. Package CRUD with items
///   C. Quotation lifecycle: create, convert to reservation, duplicate-convert guard
///   D. KPI endpoint: ADR and RevPAR calculations
///   E. Excel export endpoints return xlsx content-type
/// </summary>
public class Phase10_5Tests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase10_5Tests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p105")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase105 User",
            PhoneNumber: "+66800009999",
            OrganizationName: $"P105 Org {Guid.NewGuid():N}"[..20]
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
            Name: "P105 Hotel", Code: $"P105{Guid.NewGuid():N}"[..4],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd", ContactPhone: "+6680000",
            ContactEmail: "p105@test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId)
    {
        var req = new CreateGuestRequest(
            FullName: "P105 Guest", Email: $"p105_{Guid.NewGuid():N}@g.com",
            Phone: null, NationalId: null, PassportNumber: null,
            Nationality: null, Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propId)
    {
        var req = new CreateRoomTypeRequest(
            Name: "Standard P105", Code: $"S105{Guid.NewGuid():N}"[..4],
            BaseRate: 1500m, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateServiceItemAsync(string token, Guid orgId, Guid propId,
        string name = "Breakfast", decimal price = 250m, string category = "Meal")
    {
        var req = new CreateServiceItemRequest(name, null, price, "per person", category);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/service-items", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ServiceItemDto>(_jsonOptions))!.Id;
    }

    // ── A. ServiceItem CRUD ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateServiceItem_ReturnsCreated()
    {
        var (token, orgId) = await RegisterAsync("si_create");
        var propId = await CreatePropertyAsync(token, orgId);

        var req = new CreateServiceItemRequest("Breakfast Buffet", "Full buffet", 350m, "per person", "Meal");
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/service-items", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ServiceItemDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("Breakfast Buffet", dto.Name);
        Assert.Equal(350m, dto.Price);
        Assert.Equal("Meal", dto.Category);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task UpdateServiceItem_ChangesPrice()
    {
        var (token, orgId) = await RegisterAsync("si_update");
        var propId = await CreatePropertyAsync(token, orgId);
        var itemId = await CreateServiceItemAsync(token, orgId, propId, "Spa", 800m, "Spa");

        var req = new UpdateServiceItemRequest("Spa Deluxe", null, 1200m, "per session", "Spa");
        var res = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/service-items/{itemId}", token, orgId, req));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ServiceItemDto>(_jsonOptions);
        Assert.Equal("Spa Deluxe", dto!.Name);
        Assert.Equal(1200m, dto.Price);
    }

    [Fact]
    public async Task DeactivateServiceItem_ReturnsNoContent_And_HiddenFromActiveList()
    {
        var (token, orgId) = await RegisterAsync("si_deact");
        var propId = await CreatePropertyAsync(token, orgId);
        var itemId = await CreateServiceItemAsync(token, orgId, propId, "Transfer", 500m, "Transfer");

        var del = await _client.SendAsync(Auth(HttpMethod.Delete,
            $"/api/v1/properties/{propId}/service-items/{itemId}", token, orgId));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        // Should not appear in default (activeOnly=true) list
        var list = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/service-items", token, orgId));
        var body = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task ServiceItems_CrossProperty_AreIsolated()
    {
        var (tokenA, orgA) = await RegisterAsync("si_isoA");
        var (tokenB, orgB) = await RegisterAsync("si_isoB");
        var propA = await CreatePropertyAsync(tokenA, orgA);
        var propB = await CreatePropertyAsync(tokenB, orgB);

        await CreateServiceItemAsync(tokenA, orgA, propA, "MassageA", 600m, "Spa");

        // Org B should see zero items for propB
        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/service-items", tokenB, orgB));
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(0, body.GetProperty("total").GetInt32());
    }

    // ── B. Package CRUD ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreatePackage_WithItems_ReturnsCreated()
    {
        var (token, orgId) = await RegisterAsync("pkg_create");
        var propId = await CreatePropertyAsync(token, orgId);
        var svcId = await CreateServiceItemAsync(token, orgId, propId, "Breakfast", 250m, "Meal");

        var req = new CreatePackageRequest(
            Name: "BB Plan",
            Description: "Bed and Breakfast",
            MealPlan: "BedAndBreakfast",
            RateAddOn: 500m,
            Items: [new PackageItemRequest(svcId, 2, "per guest")]
        );
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/packages", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<PackageDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("BB Plan", dto.Name);
        Assert.Equal("BedAndBreakfast", dto.MealPlan);
        Assert.Equal(500m, dto.RateAddOn);
        Assert.Single(dto.Items);
        Assert.Equal(2, dto.Items[0].Quantity);
    }

    [Fact]
    public async Task GetPackage_IncludesServiceItemNames()
    {
        var (token, orgId) = await RegisterAsync("pkg_get");
        var propId = await CreatePropertyAsync(token, orgId);
        var svcId = await CreateServiceItemAsync(token, orgId, propId, "Dinner", 450m, "Meal");

        var createReq = new CreatePackageRequest("HB Plan", null, "HalfBoard", 900m,
            [new PackageItemRequest(svcId, 1, null)]);
        var createRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/packages", token, orgId, createReq));
        var pkg = await createRes.Content.ReadFromJsonAsync<PackageDto>(_jsonOptions);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/packages/{pkg!.Id}", token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<PackageDto>(_jsonOptions);
        Assert.Equal("Dinner", dto!.Items[0].ServiceItemName);
    }

    // ── C. Quotation lifecycle ────────────────────────────────────────────────

    [Fact]
    public async Task CreateQuotation_ReturnsCreated()
    {
        var (token, orgId) = await RegisterAsync("qt_create");
        var propId = await CreatePropertyAsync(token, orgId);
        var guestId = await CreateGuestAsync(token, orgId);

        var req = new CreateQuotationRequest(
            GuestId: guestId,
            RoomTypeId: null,
            CheckInDate: "2027-03-01",
            CheckOutDate: "2027-03-04",
            Adults: 2,
            Children: 0,
            RatePlanId: null,
            QuotedRatePerNight: 1500m,
            TotalAmount: 4500m,
            Currency: "THB",
            ExpiryDate: "2027-02-20",
            Notes: "Corporate rate");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, req));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<QuotationDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.StartsWith("QT-", dto.QuotationNumber);
        Assert.Equal("Draft", dto.Status);
        Assert.Equal(3, dto.Nights);
        Assert.Equal(4500m, dto.TotalAmount);
    }

    [Fact]
    public async Task ConvertQuotation_CreatesReservation()
    {
        var (token, orgId) = await RegisterAsync("qt_conv");
        var propId = await CreatePropertyAsync(token, orgId);
        var guestId = await CreateGuestAsync(token, orgId);

        var createReq = new CreateQuotationRequest(
            guestId, null, "2027-04-10", "2027-04-13",
            2, 0, null, 1200m, 3600m, "THB", null, null);
        var createRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, createReq));
        var qt = await createRes.Content.ReadFromJsonAsync<QuotationDto>(_jsonOptions);

        var convertRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations/{qt!.Id}/convert", token, orgId,
            new ConvertQuotationRequest(null)));

        Assert.Equal(HttpStatusCode.OK, convertRes.StatusCode);
        var body = await convertRes.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.NotEqual(Guid.Empty, Guid.Parse(body.GetProperty("reservationId").GetString()!));

        // Quotation should now be "Converted"
        var getRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/quotations/{qt.Id}", token, orgId));
        var updated = await getRes.Content.ReadFromJsonAsync<QuotationDto>(_jsonOptions);
        Assert.Equal("Converted", updated!.Status);
        Assert.NotNull(updated.ConvertedReservationId);
    }

    [Fact]
    public async Task ConvertQuotation_AlreadyConverted_Returns409()
    {
        var (token, orgId) = await RegisterAsync("qt_dupconv");
        var propId = await CreatePropertyAsync(token, orgId);
        var guestId = await CreateGuestAsync(token, orgId);

        var createReq = new CreateQuotationRequest(
            guestId, null, "2027-05-01", "2027-05-03",
            1, 0, null, 1000m, 2000m, "THB", null, null);
        var qt = await (await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, createReq)))
            .Content.ReadFromJsonAsync<QuotationDto>(_jsonOptions);

        // First convert — should succeed
        var first = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations/{qt!.Id}/convert", token, orgId,
            new ConvertQuotationRequest(null)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second convert — should return 409
        var second = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations/{qt.Id}/convert", token, orgId,
            new ConvertQuotationRequest(null)));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CancelQuotation_ReturnsNoContent()
    {
        var (token, orgId) = await RegisterAsync("qt_cancel");
        var propId = await CreatePropertyAsync(token, orgId);
        var guestId = await CreateGuestAsync(token, orgId);

        var createReq = new CreateQuotationRequest(
            guestId, null, "2027-06-01", "2027-06-04",
            2, 0, null, 1500m, 4500m, "THB", null, null);
        var qt = await (await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, createReq)))
            .Content.ReadFromJsonAsync<QuotationDto>(_jsonOptions);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations/{qt!.Id}/cancel", token, orgId));
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    [Fact]
    public async Task ListQuotations_ExcludesExpiredWhenRequested()
    {
        var (token, orgId) = await RegisterAsync("qt_list");
        var propId = await CreatePropertyAsync(token, orgId);
        var guestId = await CreateGuestAsync(token, orgId);

        // Create one expired (past expiry) and one with no expiry (never expires)
        var expiredReq = new CreateQuotationRequest(
            guestId, null, "2027-07-01", "2027-07-04",
            2, 0, null, 1000m, 3000m, "THB", "2020-01-01", null);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, expiredReq));

        // null expiry = never expires; kept by excludeExpired filter regardless of DateOnly comparison
        var noExpiryReq = new CreateQuotationRequest(
            guestId, null, "2027-08-01", "2027-08-04",
            2, 0, null, 1200m, 3600m, "THB", null, null);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/quotations", token, orgId, noExpiryReq));

        // Without filter: 2 quotations
        var all = await (await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/quotations", token, orgId)))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(2, all.GetProperty("total").GetInt32());

        // With excludeExpired=true: only 1 (the null-expiry one is kept)
        var filtered = await (await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/quotations?excludeExpired=true", token, orgId)))
            .Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
    }

    // ── D. KPI (ADR / RevPAR) ─────────────────────────────────────────────────

    [Fact]
    public async Task GetKpis_WithNoRevenue_ReturnsZeroAdrAndRevpar()
    {
        var (token, orgId) = await RegisterAsync("kpi_zero");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/reports/kpis?startDate=2027-01-01&endDate=2027-01-07",
            token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<KpiReportDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal(0m, dto.Adr);
        Assert.Equal(0m, dto.RevPar);
        Assert.Equal(0m, dto.TotalRoomRevenue);
    }

    [Fact]
    public async Task GetKpis_ReturnsCorrectDateRange()
    {
        var (token, orgId) = await RegisterAsync("kpi_range");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/reports/kpis?startDate=2027-02-01&endDate=2027-02-28",
            token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<KpiReportDto>(_jsonOptions);
        Assert.Equal("2027-02-01", dto!.StartDate);
        Assert.Equal("2027-02-28", dto.EndDate);
    }

    // ── E. Excel export ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExportOccupancy_ReturnsXlsxContentType()
    {
        var (token, orgId) = await RegisterAsync("xlsx_occ");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/reports/occupancy/export?startDate=2027-01-01&endDate=2027-01-07",
            token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        // XLSX magic bytes: PK (zip archive)
        Assert.True(bytes.Length > 4);
        Assert.Equal(0x50, bytes[0]); // 'P'
        Assert.Equal(0x4B, bytes[1]); // 'K'
    }

    [Fact]
    public async Task ExportRevenue_ReturnsXlsxContentType()
    {
        var (token, orgId) = await RegisterAsync("xlsx_rev");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/reports/revenue/export?startDate=2027-01-01&endDate=2027-01-31",
            token, orgId));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            res.Content.Headers.ContentType?.MediaType);

        var bytes = await res.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 4);
        Assert.Equal(0x50, bytes[0]);
        Assert.Equal(0x4B, bytes[1]);
    }
}

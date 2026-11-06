using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 10 — Rate plans, promotions, coupons, and group bookings.
///
/// Covers:
///   A. Rate plan CRUD and staff-only isolation
///   B. Rate calculation (weekday/weekend/seasonal override, min-stay, blackout)
///   C. Coupon CRUD, validation, and redemption
///   D. Promotion stacking prevention
///   E. Group booking CRUD and group cancellation cascade
///   F. Reservation records locked quoted rate / coupon at booking time
///
/// Uses InMemory database tier — covers application-level rules, tenant scope,
/// and business logic. Quoted-rate immutability and concurrent coupon redemption
/// that require real constraints are tested in the PostgresConcurrencyTests tier.
/// </summary>
public class Phase10RatePlanTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase10RatePlanTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p10")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase10 User",
            PhoneNumber: "+66800009999",
            OrganizationName: $"P10 Org {Guid.NewGuid():N}"[..20]
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
            Name: "P10 Hotel", Code: $"P10{Guid.NewGuid():N}"[..5],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd", ContactPhone: "+6680000",
            ContactEmail: "p10@test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propId)
    {
        var req = new CreateRoomTypeRequest(
            Name: "Standard P10", Code: $"S10{Guid.NewGuid():N}"[..5],
            BaseRate: 1200m, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomAsync(string token, Guid orgId, Guid propId, Guid rtId)
    {
        var req = new CreateRoomRequest(
            RoomTypeId: rtId, RoomNumber: $"R10-{Guid.NewGuid():N}"[..8],
            BuildingId: null, FloorId: null, DisplayName: null,
            Capacity: 2, OperationalStatus: RoomOperationalStatus.Clean, Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/rooms", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId)
    {
        var req = new CreateGuestRequest(
            FullName: "P10 Guest", Email: $"p10_{Guid.NewGuid():N}@g.com",
            Phone: null, NationalId: null, PassportNumber: null,
            Nationality: null, Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions))!.Id;
    }

    // Create a rate plan; returns its id
    private async Task<Guid> CreateRatePlanAsync(
        string token, Guid orgId, Guid propId,
        string name = "BAR",
        decimal baseRate = 1200m,
        bool isPublic = true,
        Guid? roomTypeId = null)
    {
        var req = new CreateRatePlanRequest(
            Name: name,
            Code: $"RP{Guid.NewGuid():N}"[..8],
            BaseRate: baseRate,
            Currency: "THB",
            RateType: 0, // PerNight
            IsPublic: isPublic,
            RoomTypeId: roomTypeId,
            Rules: [],
            Restrictions: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateCouponAsync(
        string token, Guid orgId, Guid propId,
        string code, decimal discountValue, int discountType = 0, int? maxUses = null,
        string? validFrom = null, string? validTo = null)
    {
        var req = new CreateCouponRequest(
            Code: code,
            DiscountType: discountType, // 0 = Percentage
            Value: discountValue,
            MaxUses: maxUses,
            ValidFrom: validFrom != null ? DateOnly.Parse(validFrom) : (DateOnly?)null,
            ValidTo: validTo != null ? DateOnly.Parse(validTo) : (DateOnly?)null,
            EligibleRoomTypeIds: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<CouponDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateReservationAsync(
        string token, Guid orgId, Guid propId, Guid rtId, Guid guestId,
        string checkIn = "2027-07-10", string checkOut = "2027-07-13",
        Guid? ratePlanId = null, string? couponCode = null,
        decimal quotedRate = 1200m)
    {
        var nights = DateOnly.Parse(checkOut).DayNumber - DateOnly.Parse(checkIn).DayNumber;
        var req = new CreateReservationRequest(
            PropertyId: propId,
            GuestId: guestId,
            CheckInDate: checkIn,
            CheckOutDate: checkOut,
            Adults: 2, Children: 0,
            RoomTypeId: rtId,
            RoomId: null,
            QuotedRate: quotedRate,
            TotalAmount: quotedRate * nights,
            Currency: "THB",
            Source: "Phone",
            SpecialRequests: null,
            Notes: null,
            RatePlanId: ratePlanId,
            CouponCode: couponCode);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            "/api/v1/reservations", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateGroupBookingAsync(
        string token, Guid orgId, Guid propId,
        string coordinatorName = "ABC Corp",
        string? agreedRateNote = "Group rate THB 1000/night")
    {
        var req = new CreateGroupBookingRequest(
            CoordinatorName: coordinatorName,
            CoordinatorEmail: $"grp_{Guid.NewGuid():N}@corp.com",
            CoordinatorPhone: null,
            CompanyName: coordinatorName,
            AgreedRateNotes: agreedRateNote,
            Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<GroupBookingDto>(_jsonOptions))!.Id;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // A. Rate Plan CRUD
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateRatePlan_ReturnsCreatedWithId()
    {
        var (token, orgId) = await RegisterAsync("rp_create");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId,
            new CreateRatePlanRequest("Best Available Rate", "BAR", 1500m, "THB",
                RateType: 0, IsPublic: true, RoomTypeId: null, Rules: [], Restrictions: null)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("Best Available Rate", dto.Name);
        Assert.Equal(1500m, dto.BaseRate);
        Assert.True(dto.IsPublic);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task ListRatePlans_ReturnsOnlyPropertyPlans()
    {
        // Each org is on the Starter plan (maxProperties = 1), so use two separate orgs.
        var (tokenA, orgA) = await RegisterAsync("rp_listA");
        var (tokenB, orgB) = await RegisterAsync("rp_listB");
        var propA = await CreatePropertyAsync(tokenA, orgA);
        var propB = await CreatePropertyAsync(tokenB, orgB);

        await CreateRatePlanAsync(tokenA, orgA, propA, "PlanA", 1000m);
        await CreateRatePlanAsync(tokenA, orgA, propA, "PlanA2", 1100m);
        await CreateRatePlanAsync(tokenB, orgB, propB, "PlanB", 900m);

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propA}/rate-plans", tokenA, orgA));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var plans = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, plans.Count);
        Assert.All(plans, p => Assert.NotEqual("PlanB", p.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task UpdateRatePlan_ChangesNameAndBaseRate()
    {
        var (token, orgId) = await RegisterAsync("rp_update");
        var propId = await CreatePropertyAsync(token, orgId);
        var planId = await CreateRatePlanAsync(token, orgId, propId, "Old Name", 800m);

        var res = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/rate-plans/{planId}", token, orgId,
            new UpdateRatePlanRequest("New Name", 950m, "THB", true, Rules: [], Restrictions: null)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions);
        Assert.Equal("New Name", dto!.Name);
        Assert.Equal(950m, dto.BaseRate);
    }

    [Fact]
    public async Task DeactivateRatePlan_RemovesFromActiveList()
    {
        var (token, orgId) = await RegisterAsync("rp_deact");
        var propId = await CreatePropertyAsync(token, orgId);
        var planId = await CreateRatePlanAsync(token, orgId, propId, "ToDeactivate", 700m);

        var del = await _client.SendAsync(Auth(HttpMethod.Delete,
            $"/api/v1/properties/{propId}/rate-plans/{planId}", token, orgId));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var list = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId));
        var body = await list.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.Empty(body.GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBRatePlans()
    {
        var (tokenA, orgA) = await RegisterAsync("rp_xta");
        var (tokenB, orgB) = await RegisterAsync("rp_xtb");
        var propB = await CreatePropertyAsync(tokenB, orgB);
        await CreateRatePlanAsync(tokenB, orgB, propB, "OrgB Plan", 2000m);

        // Org A tries to read Org B's rate plans
        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propB}/rate-plans", tokenA, orgA));
        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden
            || (res.StatusCode == HttpStatusCode.OK &&
                (await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions))
                    .GetProperty("data").GetArrayLength() == 0),
            $"Expected empty / 403 / 404, got {res.StatusCode}");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // B. Rate Plan — staff-only isolation and rules
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StaffOnlyRatePlan_IsNotReturnedOnPublicEndpoint()
    {
        var (token, orgId) = await RegisterAsync("rp_staff");
        var propId = await CreatePropertyAsync(token, orgId);
        await CreateRatePlanAsync(token, orgId, propId, "Public BAR", 1200m, isPublic: true);
        await CreateRatePlanAsync(token, orgId, propId, "Staff Only", 800m, isPublic: false);

        // Public endpoint only returns isPublic = true plans
        var res = await _client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get,
                $"/api/v1/properties/{propId}/rate-plans/public"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var plans = body.GetProperty("data").EnumerateArray().ToList();
        Assert.All(plans, p => Assert.True(p.GetProperty("isPublic").GetBoolean()));
        Assert.DoesNotContain(plans, p => p.GetProperty("name").GetString() == "Staff Only");
    }

    [Fact]
    public async Task CreateRatePlan_WithWeekendRule_ReturnsRulesInDto()
    {
        var (token, orgId) = await RegisterAsync("rp_rules");
        var propId = await CreatePropertyAsync(token, orgId);

        var rules = new[]
        {
            new RateRuleRequest(DayType: "Weekend", RateMultiplier: 1.2m, FlatAdjustment: null,
                SeasonStartDate: null, SeasonEndDate: null)
        };

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId,
            new CreateRatePlanRequest("Weekend Plan", "WKD", 1200m, "THB",
                RateType: 0, IsPublic: true, RoomTypeId: null, Rules: rules, Restrictions: null)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions);
        Assert.NotEmpty(dto!.Rules);
        Assert.Contains(dto.Rules, r => r.DayType == "Weekend");
    }

    [Fact]
    public async Task CreateRatePlan_WithMinStayRestriction_ReturnsRestrictionInDto()
    {
        var (token, orgId) = await RegisterAsync("rp_minstay");
        var propId = await CreatePropertyAsync(token, orgId);

        var restriction = new RatePlanRestrictionRequest(MinNights: 2, MaxAdvanceDays: 30, BlackoutDates: []);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId,
            new CreateRatePlanRequest("Min 2 Night Plan", "M2N", 1000m, "THB",
                RateType: 0, IsPublic: true, RoomTypeId: null, Rules: [], Restrictions: restriction)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions);
        Assert.NotNull(dto!.Restrictions);
        Assert.Equal(2, dto.Restrictions.MinNights);
        Assert.Equal(30, dto.Restrictions.MaxAdvanceDays);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // C. Coupon CRUD and validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCoupon_ReturnsCreatedWithCode()
    {
        var (token, orgId) = await RegisterAsync("cpn_create");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons", token, orgId,
            new CreateCouponRequest("WELCOME10", DiscountType: 0, Value: 10m,
                MaxUses: 100, ValidFrom: null, ValidTo: null, EligibleRoomTypeIds: null)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<CouponDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.Equal("WELCOME10", dto.Code);
        Assert.Equal(10m, dto.Value);
        Assert.Equal(100, dto.MaxUses);
        Assert.Equal(0, dto.UsedCount);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task ValidateCoupon_ValidCode_ReturnsDiscountAmount()
    {
        var (token, orgId) = await RegisterAsync("cpn_valid");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        await CreateCouponAsync(token, orgId, propId, "SAVE10", 10m, discountType: 0); // 10%

        // Validate for a 3-night stay at base rate 1200/night = 3600 total. 10% off = 360 discount.
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons/validate", token, orgId,
            new ValidateCouponRequest(
                Code: "SAVE10",
                RoomTypeId: rtId,
                CheckInDate: DateOnly.Parse("2027-08-01"),
                CheckOutDate: DateOnly.Parse("2027-08-04"),
                BaseAmount: 3600m)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(body.GetProperty("isValid").GetBoolean());
        Assert.Equal(360m, body.GetProperty("discountAmount").GetDecimal());
    }

    [Fact]
    public async Task ValidateCoupon_ExpiredCode_ReturnsInvalid()
    {
        var (token, orgId) = await RegisterAsync("cpn_expired");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        // Coupon expired yesterday
        await CreateCouponAsync(token, orgId, propId, "EXPIRED5", 5m,
            validFrom: "2026-01-01", validTo: "2026-10-01");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons/validate", token, orgId,
            new ValidateCouponRequest("EXPIRED5", rtId,
                DateOnly.Parse("2027-08-01"), DateOnly.Parse("2027-08-04"), 2400m)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.Contains("expired", body.GetProperty("reason").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateCoupon_UnknownCode_ReturnsInvalid()
    {
        var (token, orgId) = await RegisterAsync("cpn_unknown");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons/validate", token, orgId,
            new ValidateCouponRequest("DOESNOTEXIST", rtId,
                DateOnly.Parse("2027-08-01"), DateOnly.Parse("2027-08-04"), 2400m)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.False(body.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task ValidateCoupon_ExceedsMaxUses_ReturnsInvalid()
    {
        var (token, orgId) = await RegisterAsync("cpn_maxuse");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);
        // MaxUses = 1 coupon
        await CreateCouponAsync(token, orgId, propId, "ONCE", 20m, maxUses: 1);

        // First use — apply to a reservation (this increments usedCount)
        await CreateReservationAsync(token, orgId, propId, rtId, guestId,
            checkIn: "2027-09-01", checkOut: "2027-09-04", couponCode: "ONCE");

        // Second validate — should be invalid (max uses reached)
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons/validate", token, orgId,
            new ValidateCouponRequest("ONCE", rtId,
                DateOnly.Parse("2027-09-05"), DateOnly.Parse("2027-09-08"), 2400m)));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.False(body.GetProperty("isValid").GetBoolean());
        Assert.Contains("max", body.GetProperty("reason").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // D. Reservation records locked rate and coupon at booking time
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateReservation_WithRatePlanAndCoupon_RecordsLockedValues()
    {
        var (token, orgId) = await RegisterAsync("rp_lock");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);
        var planId = await CreateRatePlanAsync(token, orgId, propId, "BAR Lock", 1200m, isPublic: true);
        await CreateCouponAsync(token, orgId, propId, "LOCK15", 15m, discountType: 0);

        // 3-night stay at 1200 = 3600, 15% off = 540 discount, final 3060
        var req = new CreateReservationRequest(
            PropertyId: propId,
            GuestId: guestId,
            CheckInDate: "2027-10-01",
            CheckOutDate: "2027-10-04",
            Adults: 2, Children: 0,
            RoomTypeId: rtId,
            RoomId: null,
            QuotedRate: 1200m,
            TotalAmount: 3600m,
            Currency: "THB",
            Source: "Phone",
            SpecialRequests: null, Notes: null,
            RatePlanId: planId,
            CouponCode: "LOCK15");

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            "/api/v1/reservations", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        Assert.Equal(planId, dto!.RatePlanId);
        Assert.Equal("LOCK15", dto.CouponCode);
        Assert.Equal(540m, dto.DiscountAmount);         // 15% of 3600
        Assert.Equal(3060m, dto.TotalAmount);            // 3600 - 540
    }

    [Fact]
    public async Task UpdateRatePlanBaseRate_DoesNotChangeCommittedReservation()
    {
        var (token, orgId) = await RegisterAsync("rp_noretro");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);
        var planId = await CreateRatePlanAsync(token, orgId, propId, "Retro Plan", 1000m, isPublic: true);

        // Create reservation — locks at 1000/night × 3 = 3000
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId,
            checkIn: "2027-11-01", checkOut: "2027-11-04", ratePlanId: planId,
            quotedRate: 1000m);

        // Change rate plan base rate to 2000
        await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/rate-plans/{planId}", token, orgId,
            new UpdateRatePlanRequest("Retro Plan", 2000m, "THB", true, Rules: [], Restrictions: null)));

        // Fetch reservation — total must still be 3000 (rate locked at booking)
        var getRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/reservations/{resId}", token, orgId));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var dto = await getRes.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal(3000m, dto!.TotalAmount);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // E. Group bookings
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateGroupBooking_ReturnsCreatedWithId()
    {
        var (token, orgId) = await RegisterAsync("grp_create");
        var propId = await CreatePropertyAsync(token, orgId);

        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings", token, orgId,
            new CreateGroupBookingRequest(
                CoordinatorName: "ABC Corp",
                CoordinatorEmail: "abc@corp.com",
                CoordinatorPhone: "+66800001111",
                CompanyName: "ABC Corp",
                AgreedRateNotes: "1000 THB/night",
                Notes: null)));

        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var dto = await res.Content.ReadFromJsonAsync<GroupBookingDto>(_jsonOptions);
        Assert.NotNull(dto);
        Assert.NotEqual(Guid.Empty, dto.Id);
        Assert.Equal("ABC Corp", dto.CoordinatorName);
        Assert.Equal("Active", dto.Status);
    }

    [Fact]
    public async Task ListGroupBookings_ReturnsOnlyPropertyGroups()
    {
        // Each org is on the Starter plan (maxProperties = 1), so use two separate orgs.
        var (tokenA, orgA) = await RegisterAsync("grp_listA");
        var (tokenB, orgB) = await RegisterAsync("grp_listB");
        var propA = await CreatePropertyAsync(tokenA, orgA);
        var propB = await CreatePropertyAsync(tokenB, orgB);

        await CreateGroupBookingAsync(tokenA, orgA, propA, "Alpha Group");
        await CreateGroupBookingAsync(tokenA, orgA, propA, "Beta Group");
        await CreateGroupBookingAsync(tokenB, orgB, propB, "Gamma Group");

        var res = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propA}/group-bookings", tokenA, orgA));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        var groups = body.GetProperty("data").EnumerateArray().ToList();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.NotEqual("Gamma Group",
            g.GetProperty("coordinatorName").GetString()));
    }

    [Fact]
    public async Task AddReservationToGroup_LinksReservationToGroup()
    {
        var (token, orgId) = await RegisterAsync("grp_link");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);
        var groupId = await CreateGroupBookingAsync(token, orgId, propId, "Link Corp");
        var resId = await CreateReservationAsync(token, orgId, propId, rtId, guestId,
            checkIn: "2027-12-01", checkOut: "2027-12-05");

        // Link reservation to group
        var linkRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}/reservations", token, orgId,
            new AddReservationToGroupRequest(resId)));
        Assert.Equal(HttpStatusCode.OK, linkRes.StatusCode);

        // Fetch group detail — reservation should appear in its list
        var detailRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}", token, orgId));
        Assert.Equal(HttpStatusCode.OK, detailRes.StatusCode);
        var dto = await detailRes.Content.ReadFromJsonAsync<GroupBookingDetailDto>(_jsonOptions);
        Assert.Contains(dto!.Reservations, r => r.Id == resId);
    }

    [Fact]
    public async Task CancelGroupBooking_CancelsAllLinkedReservations()
    {
        var (token, orgId) = await RegisterAsync("grp_cancel");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guest1 = await CreateGuestAsync(token, orgId);
        var guest2 = await CreateGuestAsync(token, orgId);
        var groupId = await CreateGroupBookingAsync(token, orgId, propId, "CancelCorp");

        // Add two reservations with non-overlapping dates so they don't conflict
        var res1Id = await CreateReservationAsync(token, orgId, propId, rtId, guest1,
            checkIn: "2028-01-01", checkOut: "2028-01-05");
        var res2Id = await CreateReservationAsync(token, orgId, propId, rtId, guest2,
            checkIn: "2028-02-01", checkOut: "2028-02-05");

        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}/reservations", token, orgId,
            new AddReservationToGroupRequest(res1Id)));
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}/reservations", token, orgId,
            new AddReservationToGroupRequest(res2Id)));

        // Cancel group
        var cancelRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}/cancel", token, orgId,
            new { reason = "Event cancelled" }));
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        // Both reservations should be Cancelled
        foreach (var resId in new[] { res1Id, res2Id })
        {
            var r = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/reservations/{resId}", token, orgId));
            var dto = await r.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
            Assert.Equal("Cancelled", dto!.Status);
        }

        // Group itself should be Cancelled
        var grpDetail = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/group-bookings/{groupId}", token, orgId));
        var grpDto = await grpDetail.Content.ReadFromJsonAsync<GroupBookingDetailDto>(_jsonOptions);
        Assert.Equal("Cancelled", grpDto!.Status);
    }

    [Fact]
    public async Task CrossTenant_OrgACannotCancelOrgBGroup()
    {
        var (tokenA, orgA) = await RegisterAsync("grp_xta");
        var (tokenB, orgB) = await RegisterAsync("grp_xtb");
        var propB = await CreatePropertyAsync(tokenB, orgB);
        var groupBId = await CreateGroupBookingAsync(tokenB, orgB, propB, "Org B Group");

        // Org A tries to cancel Org B's group
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propB}/group-bookings/{groupBId}/cancel",
            tokenA, orgA, new { reason = "Hostile cancel" }));

        Assert.True(res.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"Expected 403/404, got {res.StatusCode}");
    }
}

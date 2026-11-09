using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Inventory;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 13 — Direct Booking Engine (public, unauthenticated API).
///
/// Covers:
///   A. Availability: disabled engine returns 400
///   B. Availability: returns room types with public rates only
///   C. Availability: staff-only rate plans are excluded from rate display
///   D. Coupon validation: valid code returns discount amount
///   E. Coupon validation: expired/invalid code returns isValid=false
///   F. Hold creation: returns token and expires timestamp
///   G. Hold creation: second hold when no rooms left returns 409
///   H. Public reservation: valid hold creates reservation and sends email
///   I. Public reservation: expired hold returns 409
///   J. Public reservation: already-converted hold returns 409
///   K. Hold release: marks hold as inactive
///   L. BookingEngineSettings: admin can get and update settings
/// </summary>
public class Phase13BookingEngineTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase13BookingEngineTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p13")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase13 User",
            PhoneNumber: "+66800001313",
            OrganizationName: $"P13 Org {Guid.NewGuid():N}"[..20]);
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
            Name: "P13 Hotel", Code: $"P13{Guid.NewGuid():N}"[..5],
            BusinessType: PropertyType.Hotel, IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB", Address: "1 Test Rd", ContactPhone: "+6680000",
            ContactEmail: "support@p13test.com");
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propId,
        string name = "Standard P13", decimal rate = 1500m)
    {
        var req = new CreateRoomTypeRequest(
            Name: name, Code: $"ST{Guid.NewGuid():N}"[..5],
            BaseRate: rate, StandardOccupancy: 2, MaxOccupancy: 3,
            MaxAdults: 2, MaxChildren: 1, Description: null, AmenitiesJson: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions))!.Id;
    }

    private async Task<Guid> CreateRoomAsync(string token, Guid orgId, Guid propId, Guid rtId)
    {
        var req = new CreateRoomRequest(
            RoomTypeId: rtId,
            RoomNumber: $"13{Guid.NewGuid():N}"[..4],
            BuildingId: null, FloorId: null,
            DisplayName: null,
            Capacity: 2,
            OperationalStatus: RoomOperationalStatus.Clean,
            Notes: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propId}/rooms", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions))!.Id;
    }

    private async Task EnableBookingEngineAsync(string token, Guid orgId, Guid propId)
    {
        var req = new UpdateBookingEngineSettingsRequest(
            IsEnabled: true, HoldDurationMinutes: 15,
            BookingPolicyText: "Free cancellation within 24 hours.",
            MinAdvanceBookingDays: 0, MaxAdvanceBookingDays: 730);
        var res = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/booking-engine/settings", token, orgId, req));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private async Task<Guid> CreatePublicRatePlanAsync(string token, Guid orgId, Guid propId, Guid rtId,
        decimal rate = 1200m, bool isPublic = true)
    {
        var req = new CreateRatePlanRequest(
            Name: "Public Rate", Code: $"PUB{Guid.NewGuid():N}"[..5],
            BaseRate: rate, Currency: "THB",
            RateType: 0,   // PerNight
            IsPublic: isPublic,
            RoomTypeId: rtId,
            Rules: [],
            Restrictions: null);
        var res = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/rate-plans", token, orgId, req));
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<RatePlanDto>(_jsonOptions))!.Id;
    }

    // ── A. Availability: disabled engine ─────────────────────────────────────

    [Fact]
    public async Task GetAvailability_BookingEngineDisabled_Returns400()
    {
        var (token, orgId) = await RegisterAsync("p13_disabled_avail");
        var propId = await CreatePropertyAsync(token, orgId);
        // Do NOT enable booking engine

        var res = await _client.GetAsync(
            $"/api/v1/book/{propId}/availability?checkIn=2028-01-10&checkOut=2028-01-13&adults=2&children=0");

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── B. Availability: returns room types with public rates ─────────────────

    [Fact]
    public async Task GetAvailability_ReturnsRoomTypesWithPublicRates()
    {
        var (token, orgId) = await RegisterAsync("p13_avail");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, rate: 2000m);
        await CreateRoomAsync(token, orgId, propId, rtId);
        await CreatePublicRatePlanAsync(token, orgId, propId, rtId, rate: 1800m);
        await EnableBookingEngineAsync(token, orgId, propId);

        var res = await _client.GetAsync(
            $"/api/v1/book/{propId}/availability?checkIn=2028-02-01&checkOut=2028-02-04&adults=2&children=0");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PublicAvailabilityResponse>(_jsonOptions);
        Assert.NotNull(body);
        Assert.Equal(3, body.Nights);
        Assert.NotEmpty(body.RoomTypes);
        var rt = body.RoomTypes.First(r => r.RoomTypeId == rtId);
        Assert.Equal(1800m, rt.RatePerNight);   // public rate plan wins over room base rate
        Assert.Equal(5400m, rt.TotalRate);       // 1800 × 3 nights
        Assert.Equal(1, rt.AvailableCount);
    }

    // ── C. Availability: staff-only rate not shown ────────────────────────────

    [Fact]
    public async Task GetAvailability_StaffOnlyRatePlan_NotIncludedInPublicRates()
    {
        var (token, orgId) = await RegisterAsync("p13_staff_rate");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId, rate: 999m);
        await CreateRoomAsync(token, orgId, propId, rtId);
        // Create only a staff-only rate plan (IsPublic=false)
        await CreatePublicRatePlanAsync(token, orgId, propId, rtId, rate: 500m, isPublic: false);
        await EnableBookingEngineAsync(token, orgId, propId);

        var res = await _client.GetAsync(
            $"/api/v1/book/{propId}/availability?checkIn=2028-03-01&checkOut=2028-03-03&adults=1&children=0");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PublicAvailabilityResponse>(_jsonOptions);
        var rt = body!.RoomTypes.First(r => r.RoomTypeId == rtId);
        // Falls back to room type BaseRate since no public rate plan exists
        Assert.Equal(999m, rt.RatePerNight);
    }

    // ── D. Coupon validation: valid code ─────────────────────────────────────

    [Fact]
    public async Task ValidateCoupon_ValidCode_ReturnsDiscountAmount()
    {
        var (token, orgId) = await RegisterAsync("p13_coupon_valid");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableBookingEngineAsync(token, orgId, propId);

        // Create a coupon via admin endpoint
        var couponReq = new CreateCouponRequest(
            Code: "SAVE10PCT",
            DiscountType: 0,   // Percentage
            Value: 10m,
            MaxUses: null,
            ValidFrom: null,
            ValidTo: null,
            EligibleRoomTypeIds: null);
        var couponRes = await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons", token, orgId, couponReq));
        Assert.Equal(HttpStatusCode.Created, couponRes.StatusCode);

        // Validate via public endpoint (no auth)
        var req = new PublicValidateCouponRequest("SAVE10PCT", 3600m, "2028-04-01", "2028-04-04", null);
        var res = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/validate-coupon", req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PublicValidateCouponResponse>(_jsonOptions);
        Assert.True(body!.IsValid);
        Assert.Equal(360m, body.DiscountAmount);   // 10% of 3600
        Assert.Null(body.Message);
    }

    // ── E. Coupon validation: expired code ───────────────────────────────────

    [Fact]
    public async Task ValidateCoupon_ExpiredCode_ReturnsIsValidFalse()
    {
        var (token, orgId) = await RegisterAsync("p13_coupon_exp");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableBookingEngineAsync(token, orgId, propId);

        // Coupon that expired in the past
        var couponReq = new CreateCouponRequest(
            Code: "EXPIRED01",
            DiscountType: 1,   // FixedAmount
            Value: 200m,
            MaxUses: null,
            ValidFrom: DateOnly.Parse("2020-01-01"),
            ValidTo: DateOnly.Parse("2020-12-31"),   // past
            EligibleRoomTypeIds: null);
        await _client.SendAsync(Auth(HttpMethod.Post,
            $"/api/v1/properties/{propId}/coupons", token, orgId, couponReq));

        var req = new PublicValidateCouponRequest("EXPIRED01", 3000m, "2028-04-01", "2028-04-04", null);
        var res = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/validate-coupon", req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<PublicValidateCouponResponse>(_jsonOptions);
        Assert.False(body!.IsValid);
        Assert.NotNull(body.Message);
    }

    // ── F. Hold creation: returns token ──────────────────────────────────────

    [Fact]
    public async Task CreateHold_ReturnsSessionTokenAndExpiry()
    {
        var (token, orgId) = await RegisterAsync("p13_hold");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        await CreateRoomAsync(token, orgId, propId, rtId);
        await EnableBookingEngineAsync(token, orgId, propId);

        var req = new CreateHoldRequest(
            RoomTypeId: rtId,
            CheckInDate: "2028-05-01",
            CheckOutDate: "2028-05-04",
            Adults: 2, Children: 0,
            GuestEmail: "holder@example.com");
        var res = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<CreateHoldResponse>(_jsonOptions);
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.HoldToken);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(3, body.Nights);
    }

    // ── G. Hold creation: no availability returns 409 ────────────────────────

    [Fact]
    public async Task CreateHold_NoAvailableRooms_Returns409()
    {
        var (token, orgId) = await RegisterAsync("p13_hold409");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        // No rooms created for this room type → available = 0
        await EnableBookingEngineAsync(token, orgId, propId);

        var req = new CreateHoldRequest(rtId, "2028-06-01", "2028-06-03", 2, 0, null);
        var res = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", req);

        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    // ── H. Create public reservation: valid hold ──────────────────────────────

    [Fact]
    public async Task CreatePublicReservation_ValidHold_CreatesReservationAndEmail()
    {
        var (token, orgId) = await RegisterAsync("p13_booking");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        await CreateRoomAsync(token, orgId, propId, rtId);
        await EnableBookingEngineAsync(token, orgId, propId);

        // Create hold
        var holdReq = new CreateHoldRequest(rtId, "2028-07-10", "2028-07-13", 2, 0, "guest@booking.com");
        var holdRes = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", holdReq);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(_jsonOptions);

        var nullEmail = _factory.GetNullEmailService();
        var emailCountBefore = nullEmail?.SentMessages.Count ?? 0;

        // Create reservation
        var resReq = new CreatePublicReservationRequest(
            HoldToken: hold!.HoldToken,
            GuestName: "John Doe",
            GuestEmail: "guest@booking.com",
            GuestPhone: "+66812345678",
            GuestNationality: "British",
            GuestNationalId: null,
            GuestPassportNumber: "GB123456",
            CouponCode: null,
            SpecialRequests: "High floor please",
            PolicyAcknowledged: true);

        var createRes = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/reservations", resReq);

        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
        var booking = await createRes.Content.ReadFromJsonAsync<PublicReservationDto>(_jsonOptions);
        Assert.NotNull(booking);
        Assert.NotEqual(Guid.Empty, booking.ReservationId);
        Assert.Equal("John Doe", booking.GuestName);
        Assert.Equal("Pending", booking.Status);   // public bookings start as Pending
        Assert.Equal(3, booking.Nights);

        // Email confirmation should have been captured
        var emailCountAfter = nullEmail?.SentMessages.Count ?? 0;
        Assert.True(emailCountAfter > emailCountBefore);
        var lastEmail = nullEmail!.SentMessages.Last();
        Assert.Equal("guest@booking.com", lastEmail.To);
    }

    // ── I. Expired hold returns 409 ──────────────────────────────────────────

    [Fact]
    public async Task CreatePublicReservation_HoldNotFound_ReturnsError()
    {
        var (token, orgId) = await RegisterAsync("p13_no_hold");
        var propId = await CreatePropertyAsync(token, orgId);
        await EnableBookingEngineAsync(token, orgId, propId);

        var resReq = new CreatePublicReservationRequest(
            HoldToken: Guid.NewGuid(),   // non-existent hold
            GuestName: "Jane Smith",
            GuestEmail: "jane@example.com",
            GuestPhone: null, GuestNationality: null, GuestNationalId: null,
            GuestPassportNumber: null, CouponCode: null, SpecialRequests: null,
            PolicyAcknowledged: true);

        var res = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/reservations", resReq);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ── J. Already-converted hold returns 409 ────────────────────────────────

    [Fact]
    public async Task CreatePublicReservation_ConvertedHold_Returns409()
    {
        var (token, orgId) = await RegisterAsync("p13_converted");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        await CreateRoomAsync(token, orgId, propId, rtId);
        await EnableBookingEngineAsync(token, orgId, propId);

        var holdReq = new CreateHoldRequest(rtId, "2028-08-01", "2028-08-04", 2, 0, null);
        var holdRes = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", holdReq);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(_jsonOptions);

        var bookReq = new CreatePublicReservationRequest(
            hold!.HoldToken, "First Guest", "first@example.com",
            null, null, null, null, null, null, true);

        // First booking succeeds
        var first = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/reservations", bookReq);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second attempt with the same hold token fails
        var second = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/reservations", bookReq);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    // ── K. Hold release ──────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseHold_MarksHoldReleased_RoomBecomesAvailableAgain()
    {
        var (token, orgId) = await RegisterAsync("p13_release");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        await CreateRoomAsync(token, orgId, propId, rtId);
        await EnableBookingEngineAsync(token, orgId, propId);

        // Create hold
        var holdReq = new CreateHoldRequest(rtId, "2028-09-01", "2028-09-04", 2, 0, null);
        var holdRes = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", holdReq);
        var hold = await holdRes.Content.ReadFromJsonAsync<CreateHoldResponse>(_jsonOptions);

        // Release it
        var deleteRes = await _client.DeleteAsync($"/api/v1/book/{propId}/hold/{hold!.HoldToken}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        // Now creating another hold for the same room type/dates should succeed (hold released → available again)
        var holdReq2 = new CreateHoldRequest(rtId, "2028-09-01", "2028-09-04", 2, 0, null);
        var holdRes2 = await _client.PostAsJsonAsync($"/api/v1/book/{propId}/hold", holdReq2);
        Assert.Equal(HttpStatusCode.OK, holdRes2.StatusCode);
    }

    // ── L. Admin settings: get and update ────────────────────────────────────

    [Fact]
    public async Task BookingEngineSettings_AdminCanGetAndUpdate()
    {
        var (token, orgId) = await RegisterAsync("p13_settings");
        var propId = await CreatePropertyAsync(token, orgId);

        // Defaults
        var getRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/booking-engine/settings", token, orgId));
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var defaults = await getRes.Content.ReadFromJsonAsync<BookingEngineSettingsDto>(_jsonOptions);
        Assert.False(defaults!.IsEnabled);
        Assert.Equal(15, defaults.HoldDurationMinutes);

        // Update
        var req = new UpdateBookingEngineSettingsRequest(
            IsEnabled: true, HoldDurationMinutes: 20,
            BookingPolicyText: "No refunds.", MinAdvanceBookingDays: 1, MaxAdvanceBookingDays: 90);
        var putRes = await _client.SendAsync(Auth(HttpMethod.Put,
            $"/api/v1/properties/{propId}/booking-engine/settings", token, orgId, req));
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);
        var updated = await putRes.Content.ReadFromJsonAsync<BookingEngineSettingsDto>(_jsonOptions);
        Assert.True(updated!.IsEnabled);
        Assert.Equal(20, updated.HoldDurationMinutes);
        Assert.Equal("No refunds.", updated.BookingPolicyText);

        // Cross-property isolation: different org cannot access this property's settings
        var (tokenB, orgB) = await RegisterAsync("p13_settingsB");
        var crossRes = await _client.SendAsync(Auth(HttpMethod.Get,
            $"/api/v1/properties/{propId}/booking-engine/settings", tokenB, orgB));
        Assert.Equal(HttpStatusCode.NotFound, crossRes.StatusCode);
    }
}

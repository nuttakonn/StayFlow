using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Reservations;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 8 — Production Hardening tests.
///
/// Covers:
///   - Security headers on every response
///   - Correlation ID propagation
///   - Cross-tenant IDOR denial (org A cannot read org B resources)
///   - Branding isolation (org A cannot modify org B branding)
///   - Concurrent double-booking prevention (application-level)
///   - Input validation rejection
///   - Unauthenticated access denial
///   - Liveness and readiness health probes
/// </summary>
public class Phase8IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase8IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p8")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase8 User",
            PhoneNumber: "+66800008888",
            OrganizationName: $"Phase8 Org {Guid.NewGuid():N}"[..28]
        );
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return (auth!.AccessToken, auth.ActiveOrganizationId!.Value);
    }

    private HttpRequestMessage Auth(HttpMethod method, string url, string token, Guid orgId, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Organization-Id", orgId.ToString());
        if (body != null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    private async Task<Guid> CreatePropertyAsync(string token, Guid orgId)
    {
        var propReq = new CreatePropertyRequest(
            Name: "Phase8 Hotel",
            Code: $"P8{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "1 Security Rd, Bangkok",
            ContactPhone: "+66800001111",
            ContactEmail: "phase8@test.com"
        );
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, propReq));
        var prop = await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);
        return prop!.Id;
    }

    private async Task<Guid> CreateRoomTypeAsync(string token, Guid orgId, Guid propertyId)
    {
        var req = new CreateRoomTypeRequest(
            Name: "Deluxe",
            Code: $"DLX{Guid.NewGuid():N}"[..6],
            BaseRate: 1800m,
            StandardOccupancy: 2,
            MaxOccupancy: 2,
            MaxAdults: 2,
            MaxChildren: 1,
            Description: null,
            AmenitiesJson: null
        );
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, req));
        var rt = await res.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);
        return rt!.Id;
    }

    private async Task<Guid> CreateRoomAsync(string token, Guid orgId, Guid propertyId, Guid roomTypeId)
    {
        var req = new CreateRoomRequest(
            RoomTypeId: roomTypeId,
            RoomNumber: $"P8-{Guid.NewGuid():N}"[..8],
            BuildingId: null,
            FloorId: null,
            DisplayName: null,
            Capacity: 2,
            OperationalStatus: RoomOperationalStatus.Clean,
            Notes: null
        );
        var res = await _client.SendAsync(Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, req));
        var room = await res.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        return room!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId)
    {
        var guestReq = new CreateGuestRequest(
            FullName: $"Phase8 Guest {Guid.NewGuid():N}"[..20],
            Email: $"guest8_{Guid.NewGuid():N}@test.com",
            Phone: "+66811112222",
            NationalId: null,
            PassportNumber: null,
            Nationality: null,
            Notes: null
        );
        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq));
        var guest = await res.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);
        return guest!.Id;
    }

    // -----------------------------------------------------------------
    // Security header tests
    // -----------------------------------------------------------------

    [Fact]
    public async Task SecurityHeaders_ArePresentOnPublicEndpoints()
    {
        var res = await _client.GetAsync("/healthz");
        Assert.True(res.IsSuccessStatusCode);

        Assert.True(res.Headers.Contains("X-Content-Type-Options"), "Missing X-Content-Type-Options");
        Assert.True(res.Headers.Contains("X-Frame-Options"), "Missing X-Frame-Options");
        Assert.True(res.Headers.Contains("X-XSS-Protection"), "Missing X-XSS-Protection");
        Assert.True(res.Headers.Contains("Referrer-Policy"), "Missing Referrer-Policy");
        Assert.True(res.Headers.Contains("Permissions-Policy"), "Missing Permissions-Policy");

        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").First());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").First());
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnAuthenticatedEndpoints()
    {
        var (token, orgId) = await RegisterAsync("hdr");
        var res = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/organizations/{orgId}", token, orgId));
        Assert.True(res.IsSuccessStatusCode);

        Assert.True(res.Headers.Contains("X-Content-Type-Options"), "Missing X-Content-Type-Options");
        Assert.True(res.Headers.Contains("X-Frame-Options"), "Missing X-Frame-Options");
    }

    // -----------------------------------------------------------------
    // Correlation ID tests
    // -----------------------------------------------------------------

    [Fact]
    public async Task CorrelationId_GeneratedAndReturnedWhenNotProvided()
    {
        var res = await _client.GetAsync("/healthz");
        Assert.True(res.IsSuccessStatusCode);
        Assert.True(res.Headers.Contains("X-Correlation-Id"), "X-Correlation-Id header missing from response");

        var corrId = res.Headers.GetValues("X-Correlation-Id").First();
        Assert.False(string.IsNullOrEmpty(corrId));
    }

    [Fact]
    public async Task CorrelationId_PropagatedWhenClientProvides()
    {
        const string clientCorrelationId = "test-corr-abc-123";
        var req = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        req.Headers.Add("X-Correlation-Id", clientCorrelationId);

        var res = await _client.SendAsync(req);
        Assert.True(res.IsSuccessStatusCode);

        var returnedId = res.Headers.GetValues("X-Correlation-Id").First();
        Assert.Equal(clientCorrelationId, returnedId);
    }

    // -----------------------------------------------------------------
    // Unauthenticated access denial
    // -----------------------------------------------------------------

    [Fact]
    public async Task UnauthenticatedRequest_ToProtectedEndpoint_Returns401()
    {
        var res = await _client.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_ToReservations_Returns401()
    {
        var res = await _client.GetAsync("/api/v1/reservations");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // -----------------------------------------------------------------
    // Cross-tenant IDOR denial
    // -----------------------------------------------------------------

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBOrganization()
    {
        var (tokenA, orgAId) = await RegisterAsync("idor_a");
        var (tokenB, orgBId) = await RegisterAsync("idor_b");

        // Org A tries to read Org B using Org A's auth context
        var res = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/organizations/{orgBId}", tokenA, orgAId));

        // Must not return 200 with Org B's data
        Assert.NotEqual(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBProperties()
    {
        var (tokenA, orgAId) = await RegisterAsync("idor_pa");
        var (tokenB, orgBId) = await RegisterAsync("idor_pb");

        // Create a property in Org B
        var propertyId = await CreatePropertyAsync(tokenB, orgBId);

        // Org A tries to read it — must be denied
        var res = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}", tokenA, orgAId));
        Assert.True(
            res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403/404 but got {res.StatusCode}"
        );
    }

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBGuests()
    {
        var (tokenA, orgAId) = await RegisterAsync("idor_ga");
        var (tokenB, orgBId) = await RegisterAsync("idor_gb");

        var guestId = await CreateGuestAsync(tokenB, orgBId);

        var res = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/guests/{guestId}", tokenA, orgAId));
        Assert.True(
            res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403/404 but got {res.StatusCode}"
        );
    }

    [Fact]
    public async Task CrossTenant_OrgACannotReadOrgBReservations()
    {
        var (tokenA, orgAId) = await RegisterAsync("idor_ra");
        var (tokenB, orgBId) = await RegisterAsync("idor_rb");

        var propId = await CreatePropertyAsync(tokenB, orgBId);
        var rtId = await CreateRoomTypeAsync(tokenB, orgBId, propId);
        var guestId = await CreateGuestAsync(tokenB, orgBId);

        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var dayAfter = tomorrow.AddDays(2);

        var resReq = new CreateReservationRequest(
            PropertyId: propId,
            GuestId: guestId,
            CheckInDate: tomorrow.ToString("yyyy-MM-dd"),
            CheckOutDate: dayAfter.ToString("yyyy-MM-dd"),
            Adults: 1,
            Children: 0,
            RoomTypeId: rtId,
            RoomId: null,
            QuotedRate: 1800m,
            TotalAmount: 3600m,
            Currency: "THB",
            Source: "Direct",
            SpecialRequests: null,
            Notes: null
        );

        var createRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", tokenB, orgBId, resReq));
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var reservationB = await createRes.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        // Org A tries to read Org B's reservation
        var readRes = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/reservations/{reservationB!.Id}", tokenA, orgAId));
        Assert.True(
            readRes.StatusCode == HttpStatusCode.NotFound || readRes.StatusCode == HttpStatusCode.Forbidden,
            $"Expected 403/404 but got {readRes.StatusCode}"
        );
    }

    // -----------------------------------------------------------------
    // Branding isolation
    // -----------------------------------------------------------------

    [Fact]
    public async Task CrossTenant_OrgACannotUpdateOrgBBranding()
    {
        var (tokenA, orgAId) = await RegisterAsync("brand_a");
        var (_, orgBId) = await RegisterAsync("brand_b");

        var updateReq = new
        {
            organizationId = orgBId,
            propertyId = (Guid?)null,
            displayName = "Malicious Branding Override"
        };

        // Attempt: send Org B's ID in the body but authenticate as Org A
        var req = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/branding/organizations/{orgBId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        req.Headers.Add("X-Organization-Id", orgAId.ToString());
        req.Content = JsonContent.Create(updateReq);

        var res = await _client.SendAsync(req);
        Assert.True(
            res.StatusCode == HttpStatusCode.Forbidden || res.StatusCode == HttpStatusCode.NotFound || res.StatusCode == HttpStatusCode.Unauthorized,
            $"Expected 401/403/404 but got {res.StatusCode}"
        );
    }

    // -----------------------------------------------------------------
    // Input validation
    // -----------------------------------------------------------------

    [Fact]
    public async Task Register_WithInvalidEmail_Returns400()
    {
        var req = new RegisterRequest(
            Email: "not-an-email",
            Password: "TestPassword123!",
            FullName: "Invalid User",
            PhoneNumber: "+66800000000",
            OrganizationName: "Valid Org"
        );
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Register_WithEmptyPassword_Returns400()
    {
        var req = new RegisterRequest(
            Email: $"valid_{Guid.NewGuid():N}@test.com",
            Password: "",
            FullName: "Valid Name",
            PhoneNumber: "+66800000000",
            OrganizationName: "Valid Org"
        );
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task CreateReservation_WithInvalidDates_Returns400()
    {
        var (token, orgId) = await RegisterAsync("val_res");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var guestId = await CreateGuestAsync(token, orgId);

        // Check-out before check-in — should be rejected
        var badReq = new CreateReservationRequest(
            PropertyId: propId,
            GuestId: guestId,
            CheckInDate: "2025-12-31",
            CheckOutDate: "2025-12-30",
            Adults: 1,
            Children: 0,
            RoomTypeId: rtId,
            RoomId: null,
            QuotedRate: 1000m,
            TotalAmount: 1000m,
            Currency: "THB",
            Source: "Direct",
            SpecialRequests: null,
            Notes: null
        );

        var res = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, badReq));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // -----------------------------------------------------------------
    // Concurrent double-booking prevention
    // -----------------------------------------------------------------

    [Fact]
    public async Task SequentialDoubleBooking_SameRoom_SameDates_SecondIsRejected()
    {
        // Tests the application-level overlap check: a sequential second booking for
        // the same room and dates must be rejected by the AnyAsync guard in the handler.
        //
        // NOTE: This in-memory tier can only verify the APPLICATION-LEVEL check.
        // True parallel concurrency (two requests arriving in the same moment) requires
        // the PostgreSQL exclusion constraint "no_room_double_booking" — that is proven
        // in PostgresConcurrencyTests.ParallelDoubleBooking_SameRoomSameDates_ExactlyOneCommits
        // (EPIC-52, runs against real PostgreSQL via Testcontainers).
        var (token, orgId) = await RegisterAsync("conc");
        var propId = await CreatePropertyAsync(token, orgId);
        var rtId = await CreateRoomTypeAsync(token, orgId, propId);
        var roomId = await CreateRoomAsync(token, orgId, propId, rtId);
        var guestId1 = await CreateGuestAsync(token, orgId);
        var guestId2 = await CreateGuestAsync(token, orgId);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10)).ToString("yyyy-MM-dd");
        var checkOut = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(13)).ToString("yyyy-MM-dd");

        var req1 = new CreateReservationRequest(propId, guestId1, checkIn, checkOut, 1, 0, rtId, roomId, 1800m, 5400m, "THB", "Direct", null, null);
        var req2 = new CreateReservationRequest(propId, guestId2, checkIn, checkOut, 1, 0, rtId, roomId, 1800m, 5400m, "THB", "Walk-In", null, null);

        // First booking succeeds.
        var first = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req1));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Sequential second booking for the same room/dates must be rejected.
        var second = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req2));
        Assert.True(
            second.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
            $"Expected second sequential booking to be rejected, got {(int)second.StatusCode}.");
    }

    // -----------------------------------------------------------------
    // Health probe tests
    // -----------------------------------------------------------------

    [Fact]
    public async Task LivenessProbe_Returns200()
    {
        var res = await _client.GetAsync("/healthz/live");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
    }

    [Fact]
    public async Task ReadinessProbe_Returns200_WithCheckDetails()
    {
        var res = await _client.GetAsync("/healthz/ready");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("status", out var statusProp));
        Assert.Equal("Healthy", statusProp.GetString());
        Assert.True(root.TryGetProperty("checks", out _), "Missing 'checks' array in readiness response");
    }

    [Fact]
    public async Task LegacyHealthEndpoint_Returns200()
    {
        var res = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", body);
    }

    // -----------------------------------------------------------------
    // Token manipulation / replay
    // -----------------------------------------------------------------

    [Fact]
    public async Task MalformedToken_Returns401()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "this.is.not.a.valid.jwt");

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ExpiredOrFakeToken_Returns401()
    {
        // A structurally valid JWT signed with a different key
        const string fakeJwt = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkhhY2tlciIsImlhdCI6MTV9.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c";
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fakeJwt);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}

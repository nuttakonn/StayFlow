using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 14 — Security hardening.
///
/// Covers:
///   A. Password policy: short password rejected (< 8 chars)
///   B. Password policy: digits-only password rejected (no letter)
///   C. Password policy: letters-only password rejected (no digit)
///   D. Unauthenticated access to protected endpoint returns 401
///   E. Tampered JWT returns 401
///   F. Cross-tenant IDOR: user B cannot read user A's property detail
///   G. Cross-tenant IDOR: reservation list excludes another tenant's reservations
///   H. Cross-tenant IDOR: user B cannot read user A's guest profile
///   I. Security headers present on API responses
///   J. Auth endpoints have Cache-Control: no-store
///   K. Logo upload: plain text file rejected by magic-bytes check
///   L. Logo upload: HTML file rejected by magic-bytes check
/// </summary>
public class Phase14SecurityTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase14SecurityTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<(string token, Guid orgId)> RegisterAsync(string prefix = "p14")
    {
        var req = new RegisterRequest(
            Email: $"{prefix}_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase14 User",
            PhoneNumber: "+66800001414",
            OrganizationName: $"P14 Org {Guid.NewGuid():N}"[..20]);
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        return (auth!.AccessToken!, auth.ActiveOrganizationId!.Value);
    }

    private async Task<Guid> CreatePropertyAsync(string token, Guid orgId)
    {
        var req = new CreatePropertyRequest(
            Name: "P14 Hotel",
            Code: $"P14-{Guid.NewGuid():N}"[..8],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "123 Test Road",
            ContactPhone: "+6621231234",
            ContactEmail: "hotel@p14.com");
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        msg.Content = JsonContent.Create(req);
        var res = await _client.SendAsync(msg);
        res.EnsureSuccessStatusCode();
        var dto = await res.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);
        return dto!.Id;
    }

    private async Task<Guid> CreateGuestAsync(string token, Guid orgId)
    {
        var req = new { FullName = "P14 Guest", Email = $"guest_{Guid.NewGuid():N}@test.com", Nationality = "Thai" };
        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/guests");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        msg.Content = JsonContent.Create(req);
        var res = await _client.SendAsync(msg);
        res.EnsureSuccessStatusCode();
        var guest = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        return guest.GetProperty("id").GetGuid();
    }

    private HttpRequestMessage AuthorizedGet(string url, string token, Guid orgId)
    {
        var msg = new HttpRequestMessage(HttpMethod.Get, url);
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        return msg;
    }

    // ── A. Password policy: short password ───────────────────────────────────

    [Fact]
    public async Task Register_ShortPassword_Returns400()
    {
        var req = new RegisterRequest(
            Email: $"p14_short_{Guid.NewGuid():N}@test.com",
            Password: "Abc1234",   // 7 chars — under the new 8-char minimum
            FullName: "Test User",
            PhoneNumber: "+66800001414",
            OrganizationName: "Test Org");
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── B. Password policy: digits only ──────────────────────────────────────

    [Fact]
    public async Task Register_DigitsOnlyPassword_Returns400()
    {
        var req = new RegisterRequest(
            Email: $"p14_digit_{Guid.NewGuid():N}@test.com",
            Password: "12345678",  // 8 chars but no letters
            FullName: "Test User",
            PhoneNumber: "+66800001414",
            OrganizationName: "Test Org");
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── C. Password policy: letters only ─────────────────────────────────────

    [Fact]
    public async Task Register_LettersOnlyPassword_Returns400()
    {
        var req = new RegisterRequest(
            Email: $"p14_letters_{Guid.NewGuid():N}@test.com",
            Password: "abcdefghij",  // 10 chars but no digits
            FullName: "Test User",
            PhoneNumber: "+66800001414",
            OrganizationName: "Test Org");
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── D. Unauthenticated access returns 401 ────────────────────────────────

    [Fact]
    public async Task NoToken_ProtectedEndpoint_Returns401()
    {
        var res = await _client.GetAsync("/api/v1/properties");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── E. Tampered JWT returns 401 ──────────────────────────────────────────

    [Fact]
    public async Task TamperedJwt_Returns401()
    {
        var (token, orgId) = await RegisterAsync("p14_tampered");
        // Corrupt the last character of the signature segment
        var parts = token.Split('.');
        parts[^1] = parts[^1][..^1] + (parts[^1][^1] == 'A' ? 'B' : 'A');
        var tampered = string.Join('.', parts);

        using var msg = new HttpRequestMessage(HttpMethod.Get, "/api/v1/properties");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tampered);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        var res = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── F. Cross-tenant IDOR: property detail ────────────────────────────────

    [Fact]
    public async Task CrossTenant_PropertyDetail_Denied()
    {
        var (tokenA, orgA) = await RegisterAsync("p14_idor_a");
        var (tokenB, orgB) = await RegisterAsync("p14_idor_b");

        var propA = await CreatePropertyAsync(tokenA, orgA);

        // User B tries to fetch user A's property by ID using their own org header
        using var msg = AuthorizedGet($"/api/v1/properties/{propA}", tokenB, orgB);
        var res = await _client.SendAsync(msg);

        // Must be 404 (not found within tenant scope) — never 200
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ── G. Cross-tenant IDOR: reservation list ───────────────────────────────

    [Fact]
    public async Task CrossTenant_ReservationList_ExcludesOtherTenantData()
    {
        var (tokenA, orgA) = await RegisterAsync("p14_res_a");
        var (tokenB, orgB) = await RegisterAsync("p14_res_b");

        var propA = await CreatePropertyAsync(tokenA, orgA);

        // User B lists reservations — passing A's property ID in the query param (IDOR attempt)
        using var msg = AuthorizedGet($"/api/v1/reservations?propertyId={propA}", tokenB, orgB);
        var res = await _client.SendAsync(msg);

        // Must succeed (200) but return an empty list — never expose A's reservations
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var items = await res.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        // Response is a list or paged result — either way no items for propA should appear
        if (items.ValueKind == JsonValueKind.Array)
        {
            Assert.Empty(items.EnumerateArray());
        }
        else
        {
            // Paged: items array inside the wrapper
            var array = items.TryGetProperty("items", out var arr) ? arr : items.GetProperty("data");
            Assert.Empty(array.EnumerateArray());
        }
    }

    // ── H. Cross-tenant IDOR: guest profile ──────────────────────────────────

    [Fact]
    public async Task CrossTenant_GuestDetail_Denied()
    {
        var (tokenA, orgA) = await RegisterAsync("p14_guest_a");
        var (tokenB, orgB) = await RegisterAsync("p14_guest_b");

        var guestA = await CreateGuestAsync(tokenA, orgA);

        // User B tries to fetch user A's guest
        using var msg = AuthorizedGet($"/api/v1/guests/{guestA}", tokenB, orgB);
        var res = await _client.SendAsync(msg);

        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // ── I. Security headers present on API responses ──────────────────────────

    [Fact]
    public async Task SecurityHeaders_PresentOnApiResponses()
    {
        var res = await _client.GetAsync("/healthz");

        Assert.True(res.Headers.Contains("X-Content-Type-Options"),
            "X-Content-Type-Options header missing");
        Assert.Equal("nosniff",
            res.Headers.GetValues("X-Content-Type-Options").First());

        Assert.True(res.Headers.Contains("X-Frame-Options"),
            "X-Frame-Options header missing");
        Assert.Equal("DENY",
            res.Headers.GetValues("X-Frame-Options").First());

        Assert.True(res.Headers.Contains("Referrer-Policy"),
            "Referrer-Policy header missing");
    }

    // ── J. Auth endpoints have Cache-Control: no-store ────────────────────────

    [Fact]
    public async Task AuthEndpoint_HasCacheControlNoStore()
    {
        var req = new RegisterRequest(
            Email: $"p14_cache_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Cache Test",
            PhoneNumber: "+66800001414",
            OrganizationName: "Cache Test Org");
        var res = await _client.PostAsJsonAsync("/api/v1/auth/register", req);
        res.EnsureSuccessStatusCode();

        // Cache-Control: no-store must be set on auth responses
        var cacheHeader = res.Headers.TryGetValues("Cache-Control", out var vals)
            ? string.Join(", ", vals)
            : res.Content.Headers.TryGetValues("Cache-Control", out var cvals)
                ? string.Join(", ", cvals)
                : string.Empty;

        Assert.Contains("no-store", cacheHeader, StringComparison.OrdinalIgnoreCase);
    }

    // ── K. Logo upload: plain text rejected ──────────────────────────────────

    [Fact]
    public async Task BrandingUpload_PlainTextFile_Rejected()
    {
        var (token, orgId) = await RegisterAsync("p14_upload_txt");

        using var form = new MultipartFormDataContent();
        var fileBytes = Encoding.UTF8.GetBytes("This is a plain text file, not an image.");
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png"); // lying about content type
        form.Add(fileContent, "file", "logo.png");

        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/branding/upload-logo");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        msg.Content = form;

        var res = await _client.SendAsync(msg);
        // Magic-bytes check must reject this even though Content-Type says image/png
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    // ── L. Logo upload: HTML file rejected ───────────────────────────────────

    [Fact]
    public async Task BrandingUpload_HtmlFile_Rejected()
    {
        var (token, orgId) = await RegisterAsync("p14_upload_html");

        using var form = new MultipartFormDataContent();
        var htmlBytes = Encoding.UTF8.GetBytes("<html><script>alert('xss')</script></html>");
        var fileContent = new ByteArrayContent(htmlBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg"); // lying
        form.Add(fileContent, "file", "logo.jpg");

        using var msg = new HttpRequestMessage(HttpMethod.Post, "/api/v1/branding/upload-logo");
        msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msg.Headers.Add("X-Organization-Id", orgId.ToString());
        msg.Content = form;

        var res = await _client.SendAsync(msg);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}

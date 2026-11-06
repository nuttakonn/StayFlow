using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Integration tests for PUT /api/v1/auth/me/language (EPIC-49).
/// </summary>
public class UserLanguageIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public UserLanguageIntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId)> RegisterAsync()
    {
        var request = new RegisterRequest(
            Email: $"lang_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Language Test User",
            PhoneNumber: "+66890003333",
            OrganizationName: $"Lang Org {Guid.NewGuid():N}"[..25]
        );
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", request);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
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

    [Fact]
    public async Task NewUser_DefaultsToEnglish()
    {
        var (token, orgId) = await RegisterAsync();

        var res = await _client.SendAsync(Auth(HttpMethod.Get, "/api/v1/auth/me", token, orgId));
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var language = json.GetProperty("user").GetProperty("preferredLanguage").GetString();

        Assert.Equal("en", language);
    }

    [Theory]
    [InlineData("th")]
    [InlineData("en")]
    [InlineData("zh-CN")]
    public async Task UpdateLanguage_ToSupportedValue_PersistsAcrossRequests(string language)
    {
        var (token, orgId) = await RegisterAsync();

        var update = await _client.SendAsync(Auth(
            HttpMethod.Put, "/api/v1/auth/me/language", token, orgId,
            new UpdateLanguageRequest(language)));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<UserDto>(_jsonOptions);
        Assert.Equal(language, updated!.PreferredLanguage);

        // Re-read through a separate request to prove it was persisted, not echoed.
        var me = await _client.SendAsync(Auth(HttpMethod.Get, "/api/v1/auth/me", token, orgId));
        var json = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(language, json.GetProperty("user").GetProperty("preferredLanguage").GetString());
    }

    [Fact]
    public async Task UpdateLanguage_NormalizesCasing()
    {
        var (token, orgId) = await RegisterAsync();

        var update = await _client.SendAsync(Auth(
            HttpMethod.Put, "/api/v1/auth/me/language", token, orgId,
            new UpdateLanguageRequest("zh-cn")));

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await update.Content.ReadFromJsonAsync<UserDto>(_jsonOptions);
        Assert.Equal("zh-CN", updated!.PreferredLanguage);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("zh-TW")]
    [InlineData("")]
    [InlineData("not-a-language")]
    public async Task UpdateLanguage_ToUnsupportedValue_Returns400(string language)
    {
        var (token, orgId) = await RegisterAsync();

        var res = await _client.SendAsync(Auth(
            HttpMethod.Put, "/api/v1/auth/me/language", token, orgId,
            new UpdateLanguageRequest(language)));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task UpdateLanguage_Unauthenticated_Returns401()
    {
        var res = await _client.PutAsJsonAsync(
            "/api/v1/auth/me/language", new UpdateLanguageRequest("th"));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task UpdateLanguage_OnlyAffectsTheCallingUser()
    {
        // The endpoint takes the user identity from the token, never the body,
        // so one user's change must not leak into another's profile.
        var (tokenA, orgA) = await RegisterAsync();
        var (tokenB, orgB) = await RegisterAsync();

        await _client.SendAsync(Auth(
            HttpMethod.Put, "/api/v1/auth/me/language", tokenA, orgA,
            new UpdateLanguageRequest("th")));

        var meB = await _client.SendAsync(Auth(HttpMethod.Get, "/api/v1/auth/me", tokenB, orgB));
        var jsonB = await meB.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("en", jsonB.GetProperty("user").GetProperty("preferredLanguage").GetString());
    }
}

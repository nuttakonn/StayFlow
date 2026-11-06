using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

public class Phase7IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase7IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId)> RegisterOrganizationAsync()
    {
        var regRequest = new RegisterRequest(
            Email: $"phase7_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase7 Org Owner",
            PhoneNumber: "+66890001111",
            OrganizationName: $"Phase7 Hospitality {Guid.NewGuid():N}"[..30]
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
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

    private static CreatePropertyRequest NewPropertyRequest(string codePrefix) => new(
        Name: "StayFlow Test Property",
        Code: $"{codePrefix}{Guid.NewGuid():N}"[..7],
        BusinessType: PropertyType.Hotel,
        IanaTimeZone: "Asia/Bangkok",
        DefaultCurrency: "THB",
        Address: "1 Test Rd, Bangkok",
        ContactPhone: "+66890002222",
        ContactEmail: "test@stayflow.com"
    );

    [Fact]
    public async Task NewOrganization_StartsOnStarterTrial_WithExpectedEntitlements()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var res = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/organizations/{orgId}/subscription", token, orgId));
        res.EnsureSuccessStatusCode();
        var entitlements = await res.Content.ReadFromJsonAsync<EffectiveEntitlementsDto>(_jsonOptions);

        Assert.Equal("starter", entitlements!.PlanKey);
        Assert.Equal("Trialing", entitlements.Status);
        Assert.True(entitlements.IsActive);
        Assert.NotNull(entitlements.TrialEndsAtUtc);
        Assert.Equal(1, entitlements.MaxProperties);
        Assert.Contains("apartment.rentals", entitlements.Capabilities);
        Assert.DoesNotContain("branding.customDomain", entitlements.Capabilities);
    }

    [Fact]
    public async Task PropertiesMaxLimit_BlocksCreationBeyondStarterPlanLimit()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var firstRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, NewPropertyRequest("PA")));
        Assert.Equal(HttpStatusCode.Created, firstRes.StatusCode);

        // Starter plan allows at most 1 active property.
        var secondRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, NewPropertyRequest("PB")));
        Assert.Equal(HttpStatusCode.Forbidden, secondRes.StatusCode);
    }

    [Fact]
    public async Task ChangePlan_ToProfessional_RaisesPropertyLimit()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var firstRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, NewPropertyRequest("PA")));
        Assert.Equal(HttpStatusCode.Created, firstRes.StatusCode);

        var changeRes = await _client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/organizations/{orgId}/subscription/change-plan", token, orgId,
            new ChangeSubscriptionPlanRequest("professional")));
        changeRes.EnsureSuccessStatusCode();
        var entitlements = await changeRes.Content.ReadFromJsonAsync<EffectiveEntitlementsDto>(_jsonOptions);
        Assert.Equal("professional", entitlements!.PlanKey);
        Assert.Equal(5, entitlements.MaxProperties);

        // Now under the Professional plan's limit of 5.
        var secondRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, NewPropertyRequest("PB")));
        Assert.Equal(HttpStatusCode.Created, secondRes.StatusCode);
    }

    [Fact]
    public async Task CancelSubscription_BlocksFurtherPropertyCreation()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var cancelRes = await _client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/organizations/{orgId}/subscription/cancel", token, orgId,
            new CancelSubscriptionRequest("Testing cancellation")));
        cancelRes.EnsureSuccessStatusCode();
        var entitlements = await cancelRes.Content.ReadFromJsonAsync<EffectiveEntitlementsDto>(_jsonOptions);
        Assert.Equal("Cancelled", entitlements!.Status);
        Assert.False(entitlements.IsActive);

        var createRes = await _client.SendAsync(Auth(HttpMethod.Post, "/api/v1/properties", token, orgId, NewPropertyRequest("PA")));
        Assert.Equal(HttpStatusCode.PaymentRequired, createRes.StatusCode);
    }

    [Fact]
    public async Task CancelSubscription_Twice_SecondCallFails()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var firstCancel = await _client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/organizations/{orgId}/subscription/cancel", token, orgId,
            new CancelSubscriptionRequest("First cancellation")));
        Assert.True(firstCancel.IsSuccessStatusCode);

        var secondCancel = await _client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/organizations/{orgId}/subscription/cancel", token, orgId,
            new CancelSubscriptionRequest("Second cancellation")));
        Assert.Equal(HttpStatusCode.BadRequest, secondCancel.StatusCode);
    }

    [Fact]
    public async Task CrossTenant_CannotViewOrChangeAnotherOrganizationsSubscription()
    {
        var (tokenA, orgIdA) = await RegisterOrganizationAsync();
        var (tokenB, orgIdB) = await RegisterOrganizationAsync();

        // User B's token, but asking for Org A's subscription by id: the active-org check
        // in SubscriptionEndpoints must reject this regardless of what id is in the URL.
        var viewRes = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/organizations/{orgIdA}/subscription", tokenB, orgIdB));
        Assert.Equal(HttpStatusCode.Forbidden, viewRes.StatusCode);

        var changeRes = await _client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/organizations/{orgIdA}/subscription/change-plan", tokenB, orgIdB,
            new ChangeSubscriptionPlanRequest("enterprise")));
        Assert.Equal(HttpStatusCode.Forbidden, changeRes.StatusCode);

        // Org A's own entitlements are unaffected.
        var ownViewRes = await _client.SendAsync(Auth(HttpMethod.Get, $"/api/v1/organizations/{orgIdA}/subscription", tokenA, orgIdA));
        ownViewRes.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SubscriptionPlansCatalog_ListsSeededActivePlans()
    {
        var (token, orgId) = await RegisterOrganizationAsync();

        var res = await _client.SendAsync(Auth(HttpMethod.Get, "/api/v1/subscription-plans", token, orgId));
        res.EnsureSuccessStatusCode();
        var plans = await res.Content.ReadFromJsonAsync<List<SubscriptionPlanDto>>(_jsonOptions);

        Assert.Contains(plans!, p => p.Key == "starter");
        Assert.Contains(plans!, p => p.Key == "professional");
        Assert.Contains(plans!, p => p.Key == "enterprise");
    }
}

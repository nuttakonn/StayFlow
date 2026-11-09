using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Branding;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.Email;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = $"StayFlowTestDb_{Guid.NewGuid():N}";

    // Shared NullEmailService so tests can inspect SentMessages
    private readonly NullEmailService _nullEmailService =
        new(Microsoft.Extensions.Logging.Abstractions.NullLogger<NullEmailService>.Instance);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseProvider", "InMemory");

        // The production auth limiter is a single 20/minute bucket. The suite
        // registers far more users than that, so without raising the limit the
        // tests throttle each other and fail with 429 instead of their real
        // assertion. Rate-limiting behaviour itself is covered separately.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
        builder.UseSetting("RateLimiting:ApiPermitLimit", "100000");
        builder.UseSetting("RateLimiting:BookingPermitLimit", "100000");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<StayFlowDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<StayFlowDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);

                // Booking and room-assignment commands open a Serializable
                // transaction. The in-memory store has no transaction support
                // and throws unless this warning is downgraded, which turns
                // BeginTransaction into a no-op.
                //
                // Consequence: these tests verify the application-level
                // conflict check only. True concurrent isolation and the
                // "no_room_double_booking" exclusion constraint are PostgreSQL
                // features and are NOT exercised here — see EPIC-25.
                options.ConfigureWarnings(w =>
                    w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });

            // Replace the email service registration with our shared NullEmailService
            // so tests can inspect sent messages.
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(_nullEmailService);
        });
    }

    /// <summary>Returns the NullEmailService used in this test session for inspection.</summary>
    public NullEmailService? GetNullEmailService() => _nullEmailService;
}

public class Phase1IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase1IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Phase1_FullWorkflow_Auth_Organizations_Properties_Branding_Audit_TenantIsolation()
    {
        // -------------------------------------------------------------
        // Step 1: Register User A with Organization A ("Grand Siam Hospitality")
        // -------------------------------------------------------------
        var regRequestA = new RegisterRequest(
            Email: $"admin_{Guid.NewGuid():N}@grandsiam.com",
            Password: "SecurePassword123!",
            FullName: "Somchai Srisawat",
            PhoneNumber: "+66812345678",
            OrganizationName: "Grand Siam Hospitality"
        );

        var regResponseA = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequestA);
        Assert.Equal(HttpStatusCode.OK, regResponseA.StatusCode);

        var authA = await regResponseA.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.NotNull(authA);
        Assert.NotNull(authA.AccessToken);
        Assert.NotNull(authA.ActiveOrganizationId);
        Assert.Equal(OrganizationRole.Owner.ToString(), authA.ActiveRole);

        var orgAId = authA.ActiveOrganizationId.Value;
        var tokenA = authA.AccessToken;

        // -------------------------------------------------------------
        // Step 2: Verify /api/v1/auth/me with Bearer token
        // -------------------------------------------------------------
        using var requestMe = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        requestMe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);

        var responseMe = await _client.SendAsync(requestMe);
        Assert.Equal(HttpStatusCode.OK, responseMe.StatusCode);

        // -------------------------------------------------------------
        // Step 3: Create Property 1 (Hotel) in Organization A
        // -------------------------------------------------------------
        var propRequest1 = new CreatePropertyRequest(
            Name: "Grand Siam Palace Hotel",
            Code: $"GSP-{Guid.NewGuid():N}"[..8],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "123 Sukhumvit Road, Bangkok",
            ContactPhone: "+6620001111",
            ContactEmail: "info@grandsiamhotel.com"
        );

        using var requestCreateProp = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        requestCreateProp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        requestCreateProp.Headers.Add("X-Organization-Id", orgAId.ToString());
        requestCreateProp.Content = JsonContent.Create(propRequest1);

        var responseCreateProp = await _client.SendAsync(requestCreateProp);
        Assert.Equal(HttpStatusCode.Created, responseCreateProp.StatusCode);

        var createdProp = await responseCreateProp.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);
        Assert.NotNull(createdProp);
        Assert.Equal("Grand Siam Palace Hotel", createdProp.Name);
        Assert.Equal(orgAId, createdProp.OrganizationId);

        var propAId = createdProp.Id;

        // -------------------------------------------------------------
        // Step 4: Configure Branding for Organization A and Property Override
        // -------------------------------------------------------------
        var updateOrgBranding = new UpdateBrandingRequest(
            PropertyId: null,
            DisplayName: "Grand Siam Resorts",
            LogoUrl: "https://grandsiam.com/logo.png",
            FaviconUrl: "https://grandsiam.com/favicon.ico",
            PrimaryColor: "#7C2D12",
            SecondaryColor: "#F59E0B",
            LoginTitle: "Welcome to Grand Siam",
            LoginWelcomeText: "Luxury PMS",
            FooterText: "Grand Siam Group",
            SupportEmail: "support@grandsiam.com",
            SupportPhone: "+6620002222",
            ShowVendorAttribution: false
        );

        using var requestOrgBranding = new HttpRequestMessage(HttpMethod.Put, "/api/v1/branding");
        requestOrgBranding.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        requestOrgBranding.Headers.Add("X-Organization-Id", orgAId.ToString());
        requestOrgBranding.Content = JsonContent.Create(updateOrgBranding);

        var responseOrgBranding = await _client.SendAsync(requestOrgBranding);
        Assert.Equal(HttpStatusCode.OK, responseOrgBranding.StatusCode);

        // Resolve branding without property override: should get Org branding
        var responseGetOrgBranding = await _client.GetAsync($"/api/v1/branding?organizationId={orgAId}");
        Assert.Equal(HttpStatusCode.OK, responseGetOrgBranding.StatusCode);
        var resolvedOrgBranding = await responseGetOrgBranding.Content.ReadFromJsonAsync<BrandingDto>(_jsonOptions);
        Assert.NotNull(resolvedOrgBranding);
        Assert.Equal("Grand Siam Resorts", resolvedOrgBranding.DisplayName);
        Assert.Equal("#7C2D12", resolvedOrgBranding.PrimaryColor);

        // -------------------------------------------------------------
        // Step 5: Register User B with Organization B ("Phuket Beach Condos")
        // -------------------------------------------------------------
        var regRequestB = new RegisterRequest(
            Email: $"manager_{Guid.NewGuid():N}@phuketcondos.com",
            Password: "SecurePassword456!",
            FullName: "Kanya Wongsuwan",
            PhoneNumber: "+66898765432",
            OrganizationName: "Phuket Beach Condos"
        );

        var regResponseB = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequestB);
        Assert.Equal(HttpStatusCode.OK, regResponseB.StatusCode);

        var authB = await regResponseB.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        Assert.NotNull(authB);
        var orgBId = authB.ActiveOrganizationId!.Value;
        var tokenB = authB.AccessToken;

        // -------------------------------------------------------------
        // Step 6: Tenant Isolation Test!
        // User B attempts to access Organization A's property list
        // -------------------------------------------------------------
        using var requestCrossTenantList = new HttpRequestMessage(HttpMethod.Get, "/api/v1/properties");
        requestCrossTenantList.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        // User B tries to pass Org A's ID
        requestCrossTenantList.Headers.Add("X-Organization-Id", orgAId.ToString());

        var responseCrossTenantList = await _client.SendAsync(requestCrossTenantList);
        Assert.Equal(HttpStatusCode.OK, responseCrossTenantList.StatusCode);
        var orgBProperties = await responseCrossTenantList.Content.ReadFromJsonAsync<List<PropertyDto>>(_jsonOptions);
        Assert.NotNull(orgBProperties);
        // User B MUST NOT see Org A's property!
        Assert.DoesNotContain(orgBProperties, p => p.Id == propAId);

        // User B attempts to access Organization A's details directly
        using var requestCrossOrgDetail = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/organizations/{orgAId}");
        requestCrossOrgDetail.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);

        var responseCrossOrgDetail = await _client.SendAsync(requestCrossOrgDetail);
        // Must return 403 Forbidden!
        Assert.Equal(HttpStatusCode.Forbidden, responseCrossOrgDetail.StatusCode);

        // -------------------------------------------------------------
        // Step 7: Verify Audit Log captures actions for Organization A
        // -------------------------------------------------------------
        using var requestAudit = new HttpRequestMessage(HttpMethod.Get, "/api/v1/audit");
        requestAudit.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        requestAudit.Headers.Add("X-Organization-Id", orgAId.ToString());

        var responseAudit = await _client.SendAsync(requestAudit);
        Assert.Equal(HttpStatusCode.OK, responseAudit.StatusCode);

        var auditEvents = await responseAudit.Content.ReadFromJsonAsync<List<AuditEventDto>>(_jsonOptions);
        Assert.NotNull(auditEvents);
        Assert.NotEmpty(auditEvents);
        Assert.All(auditEvents, a => Assert.Equal(orgAId, a.OrganizationId));
    }
}

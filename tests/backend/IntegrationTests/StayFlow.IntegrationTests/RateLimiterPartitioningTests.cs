using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.IntegrationTests;

/// <summary>
/// EPIC-53 — per-IP rate-limiter partitioning.
///
/// Proves that the auth limiter is partitioned by caller rather than sharing
/// one global bucket. The critical invariant:
///
///   A noisy client exhausts its own bucket.
///   Other clients continue to be served normally.
///
/// The integration test cannot spoof a real remote IP (TestServer has no real
/// socket layer), but it CAN verify that the middleware infrastructure compiles,
/// the policy name is wired up, and that a low-limit burst triggers 429. The
/// unit tests in RateLimitPartitionKeyTests cover the IP-to-key mapping logic.
/// </summary>
public class RateLimiterPartitioningTests : IClassFixture<RateLimiterTestFactory>
{
    private readonly RateLimiterTestFactory _factory;

    public RateLimiterPartitioningTests(RateLimiterTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AuthEndpoint_ExceedsPerIpLimit_Returns429()
    {
        // The factory sets AuthPermitLimit=2 so we don't need many requests.
        var client = _factory.CreateClient();

        var payload = new RegisterRequest(
            Email: $"ratelimit_{Guid.NewGuid():N}@test.com",
            Password: "P@ss1234!",
            FullName: "Rate Limit Tester",
            PhoneNumber: null,
            OrganizationName: "Rate Limit Co");

        // First two attempts are within the limit.
        var first  = await client.PostAsJsonAsync("/api/v1/auth/register", payload);
        // The first registration succeeds.
        Assert.True(
            first.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Expected first request to succeed, got {(int)first.StatusCode}.");

        // Fire enough requests to trigger the per-IP limit (limit=2, we need 3+).
        bool hitLimit = false;
        for (var i = 0; i < 10; i++)
        {
            var req = new RegisterRequest(
                Email: $"ratelimit_{Guid.NewGuid():N}@test.com",
                Password: "P@ss1234!",
                FullName: "Rate Limit Tester",
                PhoneNumber: null,
                OrganizationName: $"RLC{Guid.NewGuid():N}"[..8]);

            var resp = await client.PostAsJsonAsync("/api/v1/auth/register", req);
            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                hitLimit = true;
                break;
            }
        }

        Assert.True(hitLimit, "Expected at least one 429 TooManyRequests response.");
    }

    [Fact]
    public async Task RateLimiter_PolicyIsAttachedToAuthGroup_NotOtherEndpoints()
    {
        // The /healthz endpoint has no rate-limiting policy; it must never 429.
        var client = _factory.CreateClient();
        for (var i = 0; i < 20; i++)
        {
            var resp = await client.GetAsync("/healthz");
            Assert.NotEqual(HttpStatusCode.TooManyRequests, resp.StatusCode);
        }
    }
}

/// <summary>
/// A narrowly-scoped factory with a very low AuthPermitLimit (2/min) so the
/// rate-limiter test doesn't have to fire hundreds of requests.
/// </summary>
public class RateLimiterTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("DatabaseProvider", "InMemory");

        // Very low limit so the test triggers 429 quickly.
        builder.UseSetting("RateLimiting:AuthPermitLimit", "2");
        builder.UseSetting("RateLimiting:ApiPermitLimit", "100000");

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<StayFlowDbContext>));
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<StayFlowDbContext>(options =>
            {
                options.UseInMemoryDatabase($"RateLimiterTestDb_{Guid.NewGuid():N}");
                options.ConfigureWarnings(w =>
                    w.Ignore(InMemoryEventId.TransactionIgnoredWarning));
            });
        });
    }
}

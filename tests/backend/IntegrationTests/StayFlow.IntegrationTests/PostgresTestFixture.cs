using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StayFlow.BuildingBlocks.Persistence;
using Testcontainers.PostgreSql;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Spins up a real PostgreSQL container and applies the EF migrations to it
/// (EPIC-52).
///
/// The default in-memory suite cannot exercise Serializable isolation or the
/// "no_room_double_booking" exclusion constraint, because the in-memory store
/// has neither transactions nor check/exclusion constraints. Any test that
/// asserts a *database-level* guarantee must use this fixture instead.
///
/// Requires a running Docker daemon. Tests using it are skipped when Docker
/// is unavailable rather than failing the suite — see DockerAvailable.
/// </summary>
public sealed class PostgresTestFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("stayflow_test")
        .WithUsername("stayflow")
        .WithPassword("stayflow_test_pw")
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            // No Docker on this machine / in this CI job. Record it so the
            // tests report "skipped", never a false pass and never a failure
            // that looks like a product defect.
            SkipReason = $"PostgreSQL container unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        var connectionString = _container.GetConnectionString();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("DatabaseProvider", "PostgreSQL");
            builder.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            builder.UseSetting("RateLimiting:AuthPermitLimit", "100000");
            builder.UseSetting("RateLimiting:ApiPermitLimit", "100000");
        });

        // Build the schema from the migrations, so the exclusion constraint and
        // the btree_gist extension are created exactly as they would be in
        // production. EnsureCreated would bypass them.
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        await _container.DisposeAsync();
    }
}

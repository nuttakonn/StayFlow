using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api;

public class DatabaseHealthCheck : IHealthCheck
{
    private readonly StayFlowDbContext _dbContext;

    public DatabaseHealthCheck(StayFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Lightweight connectivity check; works for both InMemory and PostgreSQL
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection is healthy")
                : HealthCheckResult.Unhealthy("Cannot connect to the database");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database health check threw an exception", ex);
        }
    }
}

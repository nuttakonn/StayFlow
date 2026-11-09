using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StayFlow.BuildingBlocks.Domain.BookingEngine;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Jobs;

/// <summary>
/// Marks expired BookingHolds as Expired every 5 minutes.
/// Keeps the holds table from accumulating stale Active records.
/// </summary>
public sealed class BookingHoldCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BookingHoldCleanupService> _logger;

    public BookingHoldCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<BookingHoldCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

            try
            {
                await ExpireStaleHoldsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "BookingHoldCleanupService tick failed");
            }
        }
    }

    internal async Task ExpireStaleHoldsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();

        var now = DateTimeOffset.UtcNow;

        var stale = await db.BookingHolds
            .IgnoreQueryFilters()
            .Where(h => h.Status == HoldStatus.Active && h.ExpiresAtUtc <= now)
            .ToListAsync(ct);

        foreach (var hold in stale)
            hold.Expire();

        if (stale.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Expired {Count} stale booking holds", stale.Count);
        }
    }
}

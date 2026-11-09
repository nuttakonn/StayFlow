using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Email;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Jobs;

/// <summary>
/// Sends daily property summary emails at the per-property configured UTC hour.
/// Runs once per hour to check whether any property's send window has arrived.
/// Each property's report is sent at most once per UTC calendar day.
/// </summary>
public sealed class DailyReportBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyReportBackgroundService> _logger;

    // Tracks the UTC dates on which the report was already sent per property
    private readonly Dictionary<Guid, DateOnly> _lastSent = [];

    public DailyReportBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DailyReportBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for the host to finish startup before first tick
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await TickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "DailyReportBackgroundService tick failed");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    internal async Task TickAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailService>();

        var nowUtc = DateTimeOffset.UtcNow;
        var currentHour = nowUtc.Hour;
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime);

        var settings = await db.EmailNotificationSettings
            .Where(s => s.SendDailyReport && s.DailyReportRecipients != string.Empty)
            .ToListAsync(ct);

        foreach (var s in settings)
        {
            // Skip if already sent today, or if not in the send window yet
            if (_lastSent.TryGetValue(s.PropertyId, out var lastDate) && lastDate == today)
                continue;
            if (s.DailyReportSendHourUtc != currentHour)
                continue;

            await SendDailyReportAsync(db, email, s.PropertyId, today.AddDays(-1), ct);
            _lastSent[s.PropertyId] = today;
        }
    }

    public static async Task SendDailyReportAsync(
        StayFlowDbContext db,
        IEmailService email,
        Guid propertyId,
        DateOnly reportDate,
        CancellationToken ct = default)
    {
        var property = await db.Properties.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == propertyId, ct);
        if (property == null) return;

        var settings = await db.EmailNotificationSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.PropertyId == propertyId, ct);
        if (settings == null) return;

        var recipients = settings.GetRecipientList();
        if (recipients.Count == 0) return;

        // --- KPIs for reportDate ---
        var totalRooms = await db.Rooms.CountAsync(r => r.PropertyId == propertyId && r.IsActive, ct);

        // CheckedIn = 2, CheckedOut = 3 — compare via int to avoid EF is-pattern issues
        var checkedInStatus = (int)ReservationStatus.CheckedIn;
        var checkedOutStatus = (int)ReservationStatus.CheckedOut;

        var occupiedRooms = await db.Reservations
            .CountAsync(r =>
                r.PropertyId == propertyId &&
                ((int)r.Status == checkedInStatus || (int)r.Status == checkedOutStatus) &&
                r.CheckInDate <= reportDate && r.CheckOutDate > reportDate, ct);

        var occupancyPct = totalRooms > 0 ? occupiedRooms * 100m / totalRooms : 0m;

        // Revenue: sum FolioItems created on reportDate for folios belonging to this property's reservations
        var reportDateStart = new DateTimeOffset(reportDate.Year, reportDate.Month, reportDate.Day, 0, 0, 0, TimeSpan.Zero);
        var reportDateEnd = reportDateStart.AddDays(1);

        var revenue = await db.FolioItems
            .Where(fi => fi.CreatedAtUtc >= reportDateStart && fi.CreatedAtUtc < reportDateEnd &&
                         db.Folios.Any(f => f.Id == fi.FolioId &&
                             db.Reservations.Any(r => r.Id == f.ReservationId && r.PropertyId == propertyId)))
            .SumAsync(fi => fi.Amount, ct);

        var adr = occupiedRooms > 0 ? revenue / occupiedRooms : 0m;
        var revPar = totalRooms > 0 ? revenue / totalRooms : 0m;

        var arrivals = await db.Reservations.CountAsync(r =>
            r.PropertyId == propertyId && r.CheckInDate == reportDate &&
            (int)r.Status >= 2, ct);

        var departures = await db.Reservations.CountAsync(r =>
            r.PropertyId == propertyId && r.CheckOutDate == reportDate &&
            (int)r.Status == 3, ct);

        var newRes = await db.Reservations.CountAsync(r =>
            r.PropertyId == propertyId &&
            DateOnly.FromDateTime(r.CreatedAtUtc.UtcDateTime) == reportDate, ct);

        var msg = EmailTemplates.DailyReport(
            toAddress: recipients[0],
            propertyName: property.Name,
            reportDate: reportDate,
            occupiedRooms: occupiedRooms,
            totalRooms: totalRooms,
            occupancyPct: occupancyPct,
            adr: adr,
            revPar: revPar,
            revenueToday: revenue,
            currency: property.DefaultCurrency,
            arrivalsToday: arrivals,
            departuresToday: departures,
            newReservations: newRes);

        // Send to all recipients (first is To, rest are Cc)
        var cc = recipients.Count > 1 ? recipients.Skip(1).ToList() : null;
        var withCc = msg with { Cc = cc };

        await email.SendAsync(withCc, ct);
    }
}

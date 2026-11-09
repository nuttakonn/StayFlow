using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record DailyTransactionItemDto(
    Guid PaymentId,
    Guid FolioId,
    string FolioNumber,
    string Method,
    decimal Amount,
    string Currency,
    string? ReferenceNumber,
    DateTimeOffset ProcessedAtUtc
);

public record DailyTransactionSummaryDto(
    string Date,
    decimal TotalAmount,
    string Currency,
    Dictionary<string, decimal> AmountByMethod,
    IReadOnlyCollection<DailyTransactionItemDto> Transactions
);

public record OccupancyDayDto(
    string Date,
    int TotalRooms,
    int OccupiedRooms,
    decimal OccupancyRate
);

public record OccupancyReportDto(
    string StartDate,
    string EndDate,
    int TotalRooms,
    decimal AverageOccupancyRate,
    IReadOnlyCollection<OccupancyDayDto> DailyBreakdown
);

public record RevenueBreakdownDto(
    string Category,
    decimal Amount,
    decimal Percentage
);

public record RevenueReportDto(
    string StartDate,
    string EndDate,
    decimal TotalRevenue,
    string Currency,
    IReadOnlyCollection<RevenueBreakdownDto> Categories
);

public record KpiReportDto(
    string StartDate,
    string EndDate,
    int TotalRooms,
    int TotalAvailableRoomNights,
    int RoomsSold,
    decimal OccupancyRate,
    decimal TotalRoomRevenue,
    string Currency,
    decimal Adr,
    decimal RevPar
);

public static class ReportingEndpoints
{
    public static RouteGroupBuilder MapReportingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/daily-transactions", GetDailyTransactionsAsync)
            .WithName("GetDailyTransactions")
            .WithSummary("Get daily payment receipts breakdown by payment method")
            .Produces<DailyTransactionSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/occupancy", GetOccupancyReportAsync)
            .WithName("GetOccupancyReport")
            .WithSummary("Get room occupancy rate report over a date range")
            .Produces<OccupancyReportDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/revenue", GetRevenueReportAsync)
            .WithName("GetRevenueReport")
            .WithSummary("Get revenue report broken down by charge category (Room, F&B, Minibar, etc.)")
            .Produces<RevenueReportDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/kpis", GetKpiReportAsync)
            .WithName("GetKpiReport")
            .WithSummary("Get ADR, RevPAR, and occupancy KPIs for a date range")
            .Produces<KpiReportDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/occupancy/export", ExportOccupancyAsync)
            .WithName("ExportOccupancy")
            .WithSummary("Export occupancy report as XLSX")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        group.MapGet("/revenue/export", ExportRevenueAsync)
            .WithName("ExportRevenue")
            .WithSummary("Export revenue report as XLSX")
            .Produces(StatusCodes.Status200OK, contentType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");

        return group;
    }

    private static async Task<IResult> GetDailyTransactionsAsync(
        Guid propertyId,
        [FromQuery] string? date,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var businessDate = DateOnly.TryParse(date, out var parsedDate)
            ? parsedDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        var currency = property?.DefaultCurrency ?? "THB";

        var startUtc = businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = businessDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var payments = await dbContext.Payments
            .Where(p => p.PropertyId == propertyId &&
                        p.Status == PaymentStatus.Completed &&
                        p.CreatedAtUtc >= startUtc &&
                        p.CreatedAtUtc <= endUtc)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var folioIds = payments.Select(p => p.FolioId).Distinct().ToList();
        var folios = await dbContext.Folios
            .Where(f => folioIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);

        var transactionItems = payments.Select(p =>
        {
            folios.TryGetValue(p.FolioId, out var f);
            return new DailyTransactionItemDto(
                p.Id,
                p.FolioId,
                f?.FolioNumber ?? "Unknown",
                p.Method.ToString(),
                p.Amount,
                p.Currency,
                p.ReferenceNumber,
                p.CreatedAtUtc
            );
        }).ToList();

        var totalAmount = transactionItems.Sum(t => t.Amount);
        var amountByMethod = transactionItems
            .GroupBy(t => t.Method)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        return Results.Ok(new DailyTransactionSummaryDto(
            businessDate.ToString("yyyy-MM-dd"),
            totalAmount,
            currency,
            amountByMethod,
            transactionItems
        ));
    }

    private static async Task<IResult> GetOccupancyReportAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var start = DateOnly.TryParse(startDate, out var parsedStart)
            ? parsedStart
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));

        var end = DateOnly.TryParse(endDate, out var parsedEnd)
            ? parsedEnd
            : DateOnly.FromDateTime(DateTime.UtcNow);

        if (end < start)
            return Results.Problem("End date must be on or after start date.", statusCode: StatusCodes.Status400BadRequest);

        var totalRooms = await dbContext.Rooms
            .CountAsync(r => r.PropertyId == propertyId && r.IsActive, ct);

        // Fetch active reservations in date window
        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow &&
                        r.CheckInDate <= end &&
                        start < r.CheckOutDate)
            .ToListAsync(ct);

        var days = new List<OccupancyDayDto>();
        var current = start;

        while (current <= end)
        {
            var occupied = reservations.Count(r => r.CheckInDate <= current && current < r.CheckOutDate);
            var rate = totalRooms > 0 ? Math.Round((decimal)occupied / totalRooms * 100m, 1) : 0m;

            days.Add(new OccupancyDayDto(
                current.ToString("yyyy-MM-dd"),
                totalRooms,
                occupied,
                rate
            ));

            current = current.AddDays(1);
        }

        var avgRate = days.Count > 0 ? Math.Round(days.Average(d => d.OccupancyRate), 1) : 0m;

        return Results.Ok(new OccupancyReportDto(
            start.ToString("yyyy-MM-dd"),
            end.ToString("yyyy-MM-dd"),
            totalRooms,
            avgRate,
            days
        ));
    }

    private static async Task<IResult> GetRevenueReportAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var start = DateOnly.TryParse(startDate, out var parsedStart)
            ? parsedStart
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        var end = DateOnly.TryParse(endDate, out var parsedEnd)
            ? parsedEnd
            : DateOnly.FromDateTime(DateTime.UtcNow);

        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        var currency = property?.DefaultCurrency ?? "THB";

        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = end.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var items = await dbContext.FolioItems
            .Where(i => i.PropertyId == propertyId &&
                        i.CreatedAtUtc >= startUtc &&
                        i.CreatedAtUtc <= endUtc)
            .ToListAsync(ct);

        var totalRevenue = items.Sum(i => i.Amount);

        var categories = items
            .GroupBy(i => i.Type.ToString())
            .Select(g =>
            {
                var sum = g.Sum(x => x.Amount);
                var pct = totalRevenue > 0 ? Math.Round(sum / totalRevenue * 100m, 1) : 0m;
                return new RevenueBreakdownDto(g.Key, sum, pct);
            })
            .OrderByDescending(c => c.Amount)
            .ToList();

        return Results.Ok(new RevenueReportDto(
            start.ToString("yyyy-MM-dd"),
            end.ToString("yyyy-MM-dd"),
            totalRevenue,
            currency,
            categories
        ));
    }

    private static async Task<IResult> GetKpiReportAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var start = DateOnly.TryParse(startDate, out var parsedStart)
            ? parsedStart
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));

        var end = DateOnly.TryParse(endDate, out var parsedEnd)
            ? parsedEnd
            : DateOnly.FromDateTime(DateTime.UtcNow);

        if (end < start)
            return Results.Problem("End date must be on or after start date.", statusCode: StatusCodes.Status400BadRequest);

        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        var currency = property?.DefaultCurrency ?? "THB";

        var totalRooms = await dbContext.Rooms
            .CountAsync(r => r.PropertyId == propertyId && r.IsActive, ct);

        var days = end.DayNumber - start.DayNumber + 1;
        var totalAvailableRoomNights = totalRooms * days;

        // Reservations that overlap the window (half-open: [CheckIn, CheckOut))
        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow &&
                        r.CheckInDate < end.AddDays(1) &&
                        start < r.CheckOutDate)
            .ToListAsync(ct);

        // Count room-nights sold within the window
        int roomsSold = 0;
        foreach (var r in reservations)
        {
            var effectiveStart = r.CheckInDate > start ? r.CheckInDate : start;
            var effectiveEnd = r.CheckOutDate < end.AddDays(1) ? r.CheckOutDate : end.AddDays(1);
            roomsSold += effectiveEnd.DayNumber - effectiveStart.DayNumber;
        }

        // Room revenue from folio items in the window
        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var totalRoomRevenue = await dbContext.FolioItems
            .Where(fi => fi.PropertyId == propertyId &&
                         fi.Type == FolioItemType.RoomCharge &&
                         fi.CreatedAtUtc >= startUtc &&
                         fi.CreatedAtUtc < endUtc)
            .SumAsync(fi => fi.Amount, ct);

        var occupancyRate = totalAvailableRoomNights > 0
            ? Math.Round((decimal)roomsSold / totalAvailableRoomNights * 100m, 2)
            : 0m;

        // ADR = Room Revenue / Rooms Sold
        var adr = roomsSold > 0 ? Math.Round(totalRoomRevenue / roomsSold, 2) : 0m;

        // RevPAR = Room Revenue / Total Available Room-Nights
        var revpar = totalAvailableRoomNights > 0
            ? Math.Round(totalRoomRevenue / totalAvailableRoomNights, 2)
            : 0m;

        return Results.Ok(new KpiReportDto(
            start.ToString("yyyy-MM-dd"),
            end.ToString("yyyy-MM-dd"),
            totalRooms,
            totalAvailableRoomNights,
            roomsSold,
            occupancyRate,
            totalRoomRevenue,
            currency,
            adr,
            revpar
        ));
    }

    private static async Task<IResult> ExportOccupancyAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var start = DateOnly.TryParse(startDate, out var ps) ? ps : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7));
        var end = DateOnly.TryParse(endDate, out var pe) ? pe : DateOnly.FromDateTime(DateTime.UtcNow);

        var totalRooms = await dbContext.Rooms.CountAsync(r => r.PropertyId == propertyId && r.IsActive, ct);

        var reservations = await dbContext.Reservations
            .Where(r => r.PropertyId == propertyId &&
                        r.Status != ReservationStatus.Cancelled &&
                        r.Status != ReservationStatus.NoShow &&
                        r.CheckInDate <= end &&
                        start < r.CheckOutDate)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Occupancy");

        // Header row
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Total Rooms";
        ws.Cell(1, 3).Value = "Occupied";
        ws.Cell(1, 4).Value = "Available";
        ws.Cell(1, 5).Value = "Occupancy %";
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        var current = start;
        while (current <= end)
        {
            var occupied = reservations.Count(r => r.CheckInDate <= current && current < r.CheckOutDate);
            var rate = totalRooms > 0 ? Math.Round((decimal)occupied / totalRooms * 100m, 1) : 0m;
            ws.Cell(row, 1).Value = current.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = totalRooms;
            ws.Cell(row, 3).Value = occupied;
            ws.Cell(row, 4).Value = totalRooms - occupied;
            ws.Cell(row, 5).Value = rate;
            current = current.AddDays(1);
            row++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var filename = $"occupancy_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
        return Results.File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }

    private static async Task<IResult> ExportRevenueAsync(
        Guid propertyId,
        [FromQuery] string? startDate,
        [FromQuery] string? endDate,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var start = DateOnly.TryParse(startDate, out var ps) ? ps : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var end = DateOnly.TryParse(endDate, out var pe) ? pe : DateOnly.FromDateTime(DateTime.UtcNow);

        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        var currency = property?.DefaultCurrency ?? "THB";

        var startUtc = start.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = end.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var items = await dbContext.FolioItems
            .Where(i => i.PropertyId == propertyId && i.CreatedAtUtc >= startUtc && i.CreatedAtUtc <= endUtc)
            .ToListAsync(ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Revenue");

        ws.Cell(1, 1).Value = "Category";
        ws.Cell(1, 2).Value = $"Amount ({currency})";
        ws.Cell(1, 3).Value = "% of Total";
        ws.Row(1).Style.Font.Bold = true;

        var totalRevenue = items.Sum(i => i.Amount);
        var categories = items
            .GroupBy(i => i.Type.ToString())
            .Select(g => (Category: g.Key, Amount: g.Sum(x => x.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        int row = 2;
        foreach (var (cat, amount) in categories)
        {
            var pct = totalRevenue > 0 ? Math.Round(amount / totalRevenue * 100m, 1) : 0m;
            ws.Cell(row, 1).Value = cat;
            ws.Cell(row, 2).Value = amount;
            ws.Cell(row, 3).Value = pct;
            row++;
        }

        // Total row
        ws.Cell(row, 1).Value = "TOTAL";
        ws.Cell(row, 2).Value = totalRevenue;
        ws.Cell(row, 3).Value = 100m;
        ws.Row(row).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var filename = $"revenue_{start:yyyyMMdd}_{end:yyyyMMdd}.xlsx";
        return Results.File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            filename);
    }
}

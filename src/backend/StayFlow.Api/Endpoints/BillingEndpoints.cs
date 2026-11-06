using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record CreateFolioRequest(
    string? Notes
);

public record AddChargeRequest(
    string Type, // RoomCharge, FoodAndBeverage, Minibar, ExtraBed, Laundry, DamageOrLoss, Adjustment, Other
    string Description,
    decimal UnitPrice,
    int Quantity
);

public record RecordPaymentRequest(
    string Method, // Cash, BankTransfer, CreditCard, PromptPay, Other
    decimal Amount,
    string? ReferenceNumber,
    string? Notes
);

public record ReversePaymentRequest(
    string Reason
);

public record FolioItemDto(
    Guid Id,
    string Type,
    string Description,
    decimal UnitPrice,
    int Quantity,
    decimal Amount,
    DateTimeOffset CreatedAtUtc
);

public record PaymentDto(
    Guid Id,
    string Method,
    decimal Amount,
    string Currency,
    string? ReferenceNumber,
    string? Notes,
    string Status,
    string? ReversalReason,
    DateTimeOffset CreatedAtUtc
);

public record FolioDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    Guid ReservationId,
    string FolioNumber,
    string Status,
    string Currency,
    decimal TotalCharges,
    decimal TotalPayments,
    decimal Balance,
    string? Notes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ClosedAtUtc,
    IReadOnlyCollection<FolioItemDto> Items,
    IReadOnlyCollection<PaymentDto> Payments
);

public static class BillingEndpoints
{
    public static RouteGroupBuilder MapBillingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/properties/{propertyId:guid}/folios", ListFoliosAsync)
            .WithName("ListFolios")
            .WithSummary("List folios for a property")
            .Produces<IEnumerable<FolioDto>>(StatusCodes.Status200OK);

        group.MapPost("/properties/{propertyId:guid}/reservations/{reservationId:guid}/folio", GetOrCreateFolioAsync)
            .WithName("GetOrCreateFolio")
            .WithSummary("Get or initialize folio for a reservation (auto-calculates room charges)")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/properties/{propertyId:guid}/folios/{folioId:guid}", GetFolioByIdAsync)
            .WithName("GetFolioById")
            .WithSummary("Get detailed folio with charges, payments, and balance")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/properties/{propertyId:guid}/folios/{folioId:guid}/charges", AddFolioChargeAsync)
            .WithName("AddFolioCharge")
            .WithSummary("Add a charge item (minibar, f&b, extra bed, etc.) to a folio")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/properties/{propertyId:guid}/folios/{folioId:guid}/payments", RecordFolioPaymentAsync)
            .WithName("RecordFolioPayment")
            .WithSummary("Record a payment (Cash, Bank Transfer, Card, PromptPay)")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/properties/{propertyId:guid}/folios/{folioId:guid}/payments/{paymentId:guid}/reverse", ReverseFolioPaymentAsync)
            .WithName("ReverseFolioPayment")
            .WithSummary("Reverse a payment with reason (audit-safe, non-destructive)")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapPost("/properties/{propertyId:guid}/folios/{folioId:guid}/close", CloseFolioAsync)
            .WithName("CloseFolio")
            .WithSummary("Close a folio upon checkout (balance must be zero)")
            .Produces<FolioDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        return group;
    }

    private static async Task<IResult> ListFoliosAsync(
        Guid propertyId,
        [FromQuery] Guid? reservationId,
        [FromQuery] string? status,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var query = dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .Where(f => f.PropertyId == propertyId);

        if (reservationId.HasValue)
            query = query.Where(f => f.ReservationId == reservationId.Value);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<FolioStatus>(status, true, out var parsedStatus))
            query = query.Where(f => f.Status == parsedStatus);

        var folios = await query.OrderByDescending(f => f.CreatedAtUtc).ToListAsync(ct);
        return Results.Ok(folios.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetOrCreateFolioAsync(
        Guid propertyId,
        Guid reservationId,
        [FromBody] CreateFolioRequest? request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var reservation = await dbContext.Reservations.FindAsync([reservationId], ct);
        if (reservation == null || reservation.PropertyId != propertyId)
            return Results.NotFound();

        // Check if folio already exists
        var existingFolio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.ReservationId == reservationId, ct);

        if (existingFolio != null)
            return Results.Ok(ToDto(existingFolio));

        // Create new Folio
        var count = await dbContext.Folios.CountAsync(f => f.PropertyId == propertyId, ct);
        var folioNumber = $"FOL-{DateTime.UtcNow:yyMM}-{count + 1:D4}";

        var folio = Folio.Create(
            tenantContext.OrganizationId.Value,
            propertyId,
            reservationId,
            folioNumber,
            reservation.Currency,
            request?.Notes ?? $"Folio for Reservation {reservationId}");

        // Auto-add room charge item
        var nights = reservation.NumberOfNights;
        var roomRate = reservation.QuotedRate;
        if (nights > 0 && roomRate > 0)
        {
            var roomCharge = folio.AddCharge(
                FolioItemType.RoomCharge,
                $"ค่าห้องพัก ({reservation.CheckInDate:dd/MM/yyyy} - {reservation.CheckOutDate:dd/MM/yyyy}) {nights} คืน",
                roomRate,
                nights);
            dbContext.FolioItems.Add(roomCharge);
        }

        dbContext.Folios.Add(folio);
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(ToDto(folio));
    }

    private static async Task<IResult> GetFolioByIdAsync(
        Guid propertyId,
        Guid folioId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var folio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.Id == folioId && f.PropertyId == propertyId, ct);

        if (folio == null) return Results.NotFound();

        return Results.Ok(ToDto(folio));
    }

    private static async Task<IResult> AddFolioChargeAsync(
        Guid propertyId,
        Guid folioId,
        [FromBody] AddChargeRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var folio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.Id == folioId && f.PropertyId == propertyId, ct);

        if (folio == null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Description))
            return Results.Problem("Description is required.", statusCode: StatusCodes.Status400BadRequest);

        if (request.Quantity <= 0)
            return Results.Problem("Quantity must be greater than zero.", statusCode: StatusCodes.Status400BadRequest);

        if (!Enum.TryParse<FolioItemType>(request.Type, true, out var chargeType))
            chargeType = FolioItemType.Other;

        try
        {
            var item = folio.AddCharge(chargeType, request.Description, request.UnitPrice, request.Quantity);
            dbContext.FolioItems.Add(item);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(ToDto(folio));
    }

    private static async Task<IResult> RecordFolioPaymentAsync(
        Guid propertyId,
        Guid folioId,
        [FromBody] RecordPaymentRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var folio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.Id == folioId && f.PropertyId == propertyId, ct);

        if (folio == null) return Results.NotFound();

        if (request.Amount <= 0)
            return Results.Problem("Payment amount must be greater than zero.", statusCode: StatusCodes.Status400BadRequest);

        if (!Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
            method = PaymentMethod.Cash;

        try
        {
            var payment = folio.RecordPayment(method, request.Amount, request.ReferenceNumber, request.Notes);
            dbContext.Payments.Add(payment);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(ToDto(folio));
    }

    private static async Task<IResult> ReverseFolioPaymentAsync(
        Guid propertyId,
        Guid folioId,
        Guid paymentId,
        [FromBody] ReversePaymentRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var folio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.Id == folioId && f.PropertyId == propertyId, ct);

        if (folio == null) return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.Problem("Reversal reason is required.", statusCode: StatusCodes.Status400BadRequest);

        try
        {
            folio.ReversePayment(paymentId, request.Reason);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(ToDto(folio));
    }

    private static async Task<IResult> CloseFolioAsync(
        Guid propertyId,
        Guid folioId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var folio = await dbContext.Folios
            .Include(f => f.Items)
            .Include(f => f.Payments)
            .FirstOrDefaultAsync(f => f.Id == folioId && f.PropertyId == propertyId, ct);

        if (folio == null) return Results.NotFound();

        try
        {
            folio.Close();
            await dbContext.SaveChangesAsync(ct);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity);
        }

        return Results.Ok(ToDto(folio));
    }

    private static FolioDto ToDto(Folio f) => new(
        f.Id,
        f.OrganizationId,
        f.PropertyId,
        f.ReservationId,
        f.FolioNumber,
        f.Status.ToString(),
        f.Currency,
        f.TotalCharges,
        f.TotalPayments,
        f.Balance,
        f.Notes,
        f.CreatedAtUtc,
        f.ClosedAtUtc,
        f.Items.OrderBy(i => i.CreatedAtUtc).Select(i => new FolioItemDto(
            i.Id,
            i.Type.ToString(),
            i.Description,
            i.UnitPrice,
            i.Quantity,
            i.Amount,
            i.CreatedAtUtc)).ToList(),
        f.Payments.OrderBy(p => p.CreatedAtUtc).Select(p => new PaymentDto(
            p.Id,
            p.Method.ToString(),
            p.Amount,
            p.Currency,
            p.ReferenceNumber,
            p.Notes,
            p.Status.ToString(),
            p.ReversalReason,
            p.CreatedAtUtc)).ToList()
    );
}

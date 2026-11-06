using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Apartments;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────
public record CreateTenantRequest(
    string FullName,
    string NationalIdOrPassport,
    string PhoneNumber,
    string? Email,
    string? EmergencyContactName,
    string? EmergencyContactPhone
);

public record TenantDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    string FullName,
    string NationalIdOrPassport,
    string PhoneNumber,
    string? Email,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    bool IsActive,
    DateTimeOffset CreatedAtUtc
);

public record CreateAgreementRequest(
    string? AgreementNumber,
    Guid TenantId,
    Guid RoomId,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyRent,
    decimal SecurityDeposit,
    int? BillingCycleDay,
    string? Notes
);

public record RecordDepositRequest(
    decimal Amount
);

public record RenewAgreementRequest(
    DateOnly NewEndDate,
    decimal? NewMonthlyRent
);

public record TerminateAgreementRequest(
    string Reason,
    decimal RefundAmount,
    decimal DeductionAmount
);

public record RentalAgreementDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    string AgreementNumber,
    Guid TenantId,
    string? TenantName,
    string? TenantPhone,
    Guid RoomId,
    string? RoomNumber,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal MonthlyRent,
    decimal SecurityDeposit,
    decimal DepositPaid,
    int BillingCycleDay,
    string Status,
    string? Notes,
    DateTimeOffset? TerminatedAtUtc,
    string? TerminationReason,
    decimal DepositRefundedAmount,
    decimal DepositDeductedAmount,
    DateTimeOffset CreatedAtUtc
);

public record RecordMeterReadingRequest(
    Guid RoomId,
    string UtilityType, // Electricity, Water
    DateOnly ReadingDate,
    string BillingMonth, // e.g. "2026-11"
    decimal PreviousReading,
    decimal CurrentReading,
    decimal UnitPrice,
    string? Notes
);

public record MeterReadingDto(
    Guid Id,
    Guid PropertyId,
    Guid RoomId,
    string? RoomNumber,
    string UtilityType,
    DateOnly ReadingDate,
    string BillingMonth,
    decimal PreviousReading,
    decimal CurrentReading,
    decimal UnitsUsed,
    decimal UnitPrice,
    decimal TotalAmount,
    string? Notes,
    DateTimeOffset CreatedAtUtc
);

public record LatestRoomMeterReadingsDto(
    Guid RoomId,
    decimal? LatestElectricityReading,
    decimal? LatestWaterReading
);

public record GenerateMonthlyInvoicesRequest(
    string BillingMonth,
    DateOnly DueDate
);

public record AddInvoiceItemRequest(
    string ItemType,
    string Description,
    decimal Amount,
    decimal UnitPrice,
    decimal Quantity
);

public record RecordInvoicePaymentRequest(
    decimal Amount,
    string Method,
    string? ReferenceNumber,
    string? Notes
);

public record MonthlyInvoiceItemDto(
    Guid Id,
    string ItemType,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount
);

public record InvoicePaymentRecordDto(
    Guid Id,
    decimal Amount,
    string Method,
    string? ReferenceNumber,
    string? Notes,
    DateTimeOffset CreatedAtUtc
);

public record MonthlyInvoiceDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    string InvoiceNumber,
    Guid AgreementId,
    string? AgreementNumber,
    Guid TenantId,
    string? TenantName,
    Guid RoomId,
    string? RoomNumber,
    string BillingMonth,
    DateOnly DueDate,
    decimal TotalAmount,
    decimal PaidAmount,
    decimal Balance,
    string Status,
    DateTimeOffset? PaidAtUtc,
    DateTimeOffset CreatedAtUtc,
    List<MonthlyInvoiceItemDto> Items,
    List<InvoicePaymentRecordDto> Payments
);

public record GenerateInvoicesSummaryDto(
    int TotalActiveAgreements,
    int GeneratedCount,
    int SkippedCount,
    List<MonthlyInvoiceDto> Invoices
);

public static class ApartmentEndpoints
{
    public static void MapApartmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1")
            .WithTags("Apartments & Monthly Rentals")
            .RequireAuthorization();

        // ── 1. Tenants ───────────────────────────────────────────────────────
        group.MapGet("/properties/{propertyId:guid}/tenants", GetTenantsAsync);
        group.MapPost("/properties/{propertyId:guid}/tenants", CreateTenantAsync);
        group.MapGet("/tenants/{id:guid}", GetTenantByIdAsync);

        // ── 2. Rental Agreements ─────────────────────────────────────────────
        group.MapGet("/properties/{propertyId:guid}/agreements", GetAgreementsAsync);
        group.MapPost("/properties/{propertyId:guid}/agreements", CreateAgreementAsync);
        group.MapGet("/agreements/{id:guid}", GetAgreementByIdAsync);
        group.MapPost("/agreements/{id:guid}/activate", ActivateAgreementAsync);
        group.MapPost("/agreements/{id:guid}/deposit", RecordDepositAsync);
        group.MapPost("/agreements/{id:guid}/renew", RenewAgreementAsync);
        group.MapPost("/agreements/{id:guid}/terminate", TerminateAgreementAsync);

        // ── 3. Meter Readings ────────────────────────────────────────────────
        group.MapGet("/properties/{propertyId:guid}/meter-readings", GetMeterReadingsAsync);
        group.MapPost("/properties/{propertyId:guid}/meter-readings", RecordMeterReadingAsync);
        group.MapGet("/properties/{propertyId:guid}/rooms/{roomId:guid}/meter-readings/latest", GetLatestMeterReadingsAsync);

        // ── 4. Monthly Invoices ──────────────────────────────────────────────
        group.MapGet("/properties/{propertyId:guid}/invoices", GetInvoicesAsync);
        group.MapPost("/properties/{propertyId:guid}/invoices/generate-monthly", GenerateMonthlyInvoicesAsync);
        group.MapGet("/invoices/{id:guid}", GetInvoiceByIdAsync);
        group.MapPost("/invoices/{id:guid}/items", AddInvoiceItemAsync);
        group.MapPost("/invoices/{id:guid}/issue", IssueInvoiceAsync);
        group.MapPost("/invoices/{id:guid}/payments", RecordInvoicePaymentAsync);
        group.MapPost("/invoices/{id:guid}/cancel", CancelInvoiceAsync);
    }

    // ── Tenant Handlers ──────────────────────────────────────────────────────
    private static async Task<IResult> GetTenantsAsync(
        [FromRoute] Guid propertyId,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var tenants = await db.Tenants
            .AsNoTracking()
            .Where(t => t.PropertyId == propertyId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new TenantDto(
                t.Id,
                t.OrganizationId,
                t.PropertyId,
                t.FullName,
                t.NationalIdOrPassport,
                t.PhoneNumber,
                t.Email,
                t.EmergencyContactName,
                t.EmergencyContactPhone,
                t.IsActive,
                t.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(tenants);
    }

    private static async Task<IResult> CreateTenantAsync(
        [FromRoute] Guid propertyId,
        [FromBody] CreateTenantRequest req,
        [FromServices] StayFlowDbContext db,
        [FromServices] ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.PhoneNumber))
            return Results.BadRequest(new { message = "ชื่อ-นามสกุล และเบอร์โทรศัพท์ เป็นข้อมูลจำเป็น" });

        var orgId = tenantContext.OrganizationId ?? Guid.Empty;

        var tenant = Tenant.Create(
            orgId,
            propertyId,
            req.FullName,
            req.NationalIdOrPassport,
            req.PhoneNumber,
            req.Email,
            req.EmergencyContactName,
            req.EmergencyContactPhone);

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/tenants/{tenant.Id}", new TenantDto(
            tenant.Id,
            tenant.OrganizationId,
            tenant.PropertyId,
            tenant.FullName,
            tenant.NationalIdOrPassport,
            tenant.PhoneNumber,
            tenant.Email,
            tenant.EmergencyContactName,
            tenant.EmergencyContactPhone,
            tenant.IsActive,
            tenant.CreatedAtUtc));
    }

    private static async Task<IResult> GetTenantByIdAsync(
        [FromRoute] Guid id,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var t = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return Results.NotFound();

        return Results.Ok(new TenantDto(
            t.Id,
            t.OrganizationId,
            t.PropertyId,
            t.FullName,
            t.NationalIdOrPassport,
            t.PhoneNumber,
            t.Email,
            t.EmergencyContactName,
            t.EmergencyContactPhone,
            t.IsActive,
            t.CreatedAtUtc));
    }

    // ── Rental Agreement Handlers ─────────────────────────────────────────────
    private static async Task<IResult> GetAgreementsAsync(
        [FromRoute] Guid propertyId,
        [FromQuery] string? status,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var query = db.RentalAgreements
            .AsNoTracking()
            .Include(a => a.Tenant)
            .Include(a => a.Room)
            .Where(a => a.PropertyId == propertyId);

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AgreementStatus>(status, true, out var parsedStatus))
        {
            query = query.Where(a => a.Status == parsedStatus);
        }

        var agreements = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new RentalAgreementDto(
                a.Id,
                a.OrganizationId,
                a.PropertyId,
                a.AgreementNumber,
                a.TenantId,
                a.Tenant != null ? a.Tenant.FullName : null,
                a.Tenant != null ? a.Tenant.PhoneNumber : null,
                a.RoomId,
                a.Room != null ? a.Room.RoomNumber : null,
                a.StartDate,
                a.EndDate,
                a.MonthlyRent,
                a.SecurityDeposit,
                a.DepositPaid,
                a.BillingCycleDay,
                a.Status.ToString(),
                a.Notes,
                a.TerminatedAtUtc,
                a.TerminationReason,
                a.DepositRefundedAmount,
                a.DepositDeductedAmount,
                a.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(agreements);
    }

    private static async Task<IResult> CreateAgreementAsync(
        [FromRoute] Guid propertyId,
        [FromBody] CreateAgreementRequest req,
        [FromServices] StayFlowDbContext db,
        [FromServices] ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (req.EndDate <= req.StartDate)
            return Results.BadRequest(new { message = "วันสิ้นสุดสัญญาต้องมากกว่าวันเริ่มต้นสัญญา" });

        // Verify Room and Tenant exist
        var room = await db.Rooms.FirstOrDefaultAsync(r => r.Id == req.RoomId && r.PropertyId == propertyId, ct);
        if (room == null) return Results.NotFound(new { message = "ไม่พบห้องพักที่ระบุ" });

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == req.TenantId && t.PropertyId == propertyId, ct);
        if (tenant == null) return Results.NotFound(new { message = "ไม่พบข้อมูลผู้เช่าที่ระบุ" });

        // Block if the room already has ANY active agreement.
        // Business rule: one active tenant per room at a time. A new agreement can only
        // be created after the existing one is terminated.
        var hasActiveAgreement = await db.RentalAgreements
            .AnyAsync(a => a.PropertyId == propertyId
                           && a.RoomId == req.RoomId
                           && a.Status == AgreementStatus.Active, ct);

        if (hasActiveAgreement)
        {
            return Results.Conflict(new { message = "ห้องพักนี้มีสัญญาเช่าที่กำลังใช้งานอยู่แล้ว ต้องรอให้สัญญาปัจจุบันสิ้นสุดก่อนจึงจะทำสัญญาใหม่ได้" });
        }

        // Secondary guard: also block Draft/Pending agreements with overlapping dates
        // to prevent racing between create → deposit → activate flows.
        var hasOverlap = await db.RentalAgreements
            .AnyAsync(a => a.PropertyId == propertyId
                           && a.RoomId == req.RoomId
                           && a.Status != AgreementStatus.Terminated
                           && a.StartDate <= req.EndDate
                           && req.StartDate <= a.EndDate, ct);

        if (hasOverlap)
        {
            return Results.Conflict(new { message = "ห้องพักนี้มีสัญญาเช่าที่ทับซ้อนกับช่วงวันที่ขอแล้ว" });
        }

        var orgId = tenantContext.OrganizationId ?? Guid.Empty;
        var agreementNumber = string.IsNullOrWhiteSpace(req.AgreementNumber)
            ? $"AGR-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(100, 999)}"
            : req.AgreementNumber.Trim().ToUpperInvariant();

        var agreement = RentalAgreement.Create(
            orgId,
            propertyId,
            agreementNumber,
            req.TenantId,
            req.RoomId,
            req.StartDate,
            req.EndDate,
            req.MonthlyRent,
            req.SecurityDeposit,
            req.BillingCycleDay ?? 1,
            req.Notes);

        db.RentalAgreements.Add(agreement);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/agreements/{agreement.Id}", new RentalAgreementDto(
            agreement.Id,
            agreement.OrganizationId,
            agreement.PropertyId,
            agreement.AgreementNumber,
            agreement.TenantId,
            tenant.FullName,
            tenant.PhoneNumber,
            agreement.RoomId,
            room.RoomNumber,
            agreement.StartDate,
            agreement.EndDate,
            agreement.MonthlyRent,
            agreement.SecurityDeposit,
            agreement.DepositPaid,
            agreement.BillingCycleDay,
            agreement.Status.ToString(),
            agreement.Notes,
            agreement.TerminatedAtUtc,
            agreement.TerminationReason,
            agreement.DepositRefundedAmount,
            agreement.DepositDeductedAmount,
            agreement.CreatedAtUtc));
    }

    private static async Task<IResult> GetAgreementByIdAsync(
        [FromRoute] Guid id,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var a = await db.RentalAgreements
            .AsNoTracking()
            .Include(x => x.Tenant)
            .Include(x => x.Room)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (a == null) return Results.NotFound();

        return Results.Ok(new RentalAgreementDto(
            a.Id,
            a.OrganizationId,
            a.PropertyId,
            a.AgreementNumber,
            a.TenantId,
            a.Tenant?.FullName,
            a.Tenant?.PhoneNumber,
            a.RoomId,
            a.Room?.RoomNumber,
            a.StartDate,
            a.EndDate,
            a.MonthlyRent,
            a.SecurityDeposit,
            a.DepositPaid,
            a.BillingCycleDay,
            a.Status.ToString(),
            a.Notes,
            a.TerminatedAtUtc,
            a.TerminationReason,
            a.DepositRefundedAmount,
            a.DepositDeductedAmount,
            a.CreatedAtUtc));
    }

    private static async Task<IResult> ActivateAgreementAsync(
        [FromRoute] Guid id,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var agreement = await db.RentalAgreements
            .Include(a => a.Room)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (agreement == null) return Results.NotFound();

        // Re-check at activation time: block if another active agreement exists for the same room.
        var hasActiveAgreement = await db.RentalAgreements
            .AnyAsync(a => a.PropertyId == agreement.PropertyId
                           && a.RoomId == agreement.RoomId
                           && a.Id != agreement.Id
                           && a.Status == AgreementStatus.Active, ct);

        if (hasActiveAgreement)
        {
            return Results.Conflict(new { message = "ไม่สามารถเปิดใช้สัญญาได้เนื่องจากห้องพักนี้มีสัญญาที่กำลังใช้งานอยู่แล้ว" });
        }

        agreement.Activate();
        await db.SaveChangesAsync(ct);

        return Results.Ok(new { message = "เปิดใช้งานสัญญาเช่าเรียบร้อยแล้ว", status = agreement.Status.ToString() });
    }

    private static async Task<IResult> RecordDepositAsync(
        [FromRoute] Guid id,
        [FromBody] RecordDepositRequest req,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agreement == null) return Results.NotFound();

        try
        {
            agreement.RecordDepositPayment(req.Amount);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "บันทึกการชำระเงินมัดจำเรียบร้อย", depositPaid = agreement.DepositPaid });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> RenewAgreementAsync(
        [FromRoute] Guid id,
        [FromBody] RenewAgreementRequest req,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var agreement = await db.RentalAgreements.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (agreement == null) return Results.NotFound();

        try
        {
            agreement.Renew(req.NewEndDate, req.NewMonthlyRent);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "ต่อสัญญาเช่าเรียบร้อยแล้ว", newEndDate = agreement.EndDate, monthlyRent = agreement.MonthlyRent });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> TerminateAgreementAsync(
        [FromRoute] Guid id,
        [FromBody] TerminateAgreementRequest req,
        [FromServices] StayFlowDbContext db,
        [FromServices] ITenantContext tenantContext,
        CancellationToken ct)
    {
        var agreement = await db.RentalAgreements
            .Include(a => a.Room)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (agreement == null) return Results.NotFound();

        try
        {
            agreement.Terminate(req.Reason, req.RefundAmount, req.DeductionAmount, DateTimeOffset.UtcNow);

            // Turnover Room: Automatically mark room as Dirty for turnover cleaning
            if (agreement.Room != null)
            {
                var history = agreement.Room.UpdateOperationalStatus(
                    RoomOperationalStatus.Dirty,
                    $"ย้ายออกเสร็จสิ้น (สัญญา {agreement.AgreementNumber}) ห้องรอทำความสะอาด",
                    tenantContext.UserId,
                    tenantContext.UserEmail);

                db.RoomStatusHistories.Add(history);
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                message = "สิ้นสุดสัญญาและเคลียร์เงินประกันเรียบร้อยแล้ว (Move-Out Settlement Completed)",
                status = agreement.Status.ToString(),
                depositRefunded = agreement.DepositRefundedAmount,
                depositDeducted = agreement.DepositDeductedAmount
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    // ── Meter Reading Handlers ────────────────────────────────────────────────
    private static async Task<IResult> GetMeterReadingsAsync(
        [FromRoute] Guid propertyId,
        [FromQuery] string? billingMonth,
        [FromQuery] Guid? roomId,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var query = db.MeterReadings
            .AsNoTracking()
            .Include(m => m.Room)
            .Where(m => m.PropertyId == propertyId);

        if (!string.IsNullOrWhiteSpace(billingMonth))
            query = query.Where(m => m.BillingMonth == billingMonth.Trim());

        if (roomId.HasValue)
            query = query.Where(m => m.RoomId == roomId.Value);

        var list = await query
            .OrderByDescending(m => m.ReadingDate)
            .Select(m => new MeterReadingDto(
                m.Id,
                m.PropertyId,
                m.RoomId,
                m.Room != null ? m.Room.RoomNumber : null,
                m.UtilityType.ToString(),
                m.ReadingDate,
                m.BillingMonth,
                m.PreviousReading,
                m.CurrentReading,
                m.UnitsUsed,
                m.UnitPrice,
                m.TotalAmount,
                m.Notes,
                m.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(list);
    }

    private static async Task<IResult> RecordMeterReadingAsync(
        [FromRoute] Guid propertyId,
        [FromBody] RecordMeterReadingRequest req,
        [FromServices] StayFlowDbContext db,
        [FromServices] ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!Enum.TryParse<UtilityType>(req.UtilityType, true, out var utilityType))
            return Results.BadRequest(new { message = "ประเภทมิเตอร์ไม่ถูกต้อง (Electricity หรือ Water เท่านั้น)" });

        if (req.CurrentReading < req.PreviousReading)
            return Results.BadRequest(new { message = $"เลขมิเตอร์ปัจจุบัน ({req.CurrentReading}) ต้องไม่น้อยกว่าเลขมิเตอร์ครั้งก่อน ({req.PreviousReading})" });

        var orgId = tenantContext.OrganizationId ?? Guid.Empty;

        // Check if already recorded for this month & utility
        var existing = await db.MeterReadings
            .FirstOrDefaultAsync(m => m.PropertyId == propertyId
                                      && m.RoomId == req.RoomId
                                      && m.UtilityType == utilityType
                                      && m.BillingMonth == req.BillingMonth.Trim(), ct);

        if (existing != null)
        {
            return Results.Conflict(new { message = $"มีการบันทึกมิเตอร์ {utilityType} ประจำรอบเดือน {req.BillingMonth} ไว้แล้ว" });
        }

        var reading = MeterReading.Record(
            orgId,
            propertyId,
            req.RoomId,
            utilityType,
            req.ReadingDate,
            req.BillingMonth,
            req.PreviousReading,
            req.CurrentReading,
            req.UnitPrice,
            tenantContext.UserId,
            req.Notes);

        db.MeterReadings.Add(reading);
        await db.SaveChangesAsync(ct);

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == req.RoomId, ct);

        return Results.Created($"/api/v1/properties/{propertyId}/meter-readings", new MeterReadingDto(
            reading.Id,
            reading.PropertyId,
            reading.RoomId,
            room?.RoomNumber,
            reading.UtilityType.ToString(),
            reading.ReadingDate,
            reading.BillingMonth,
            reading.PreviousReading,
            reading.CurrentReading,
            reading.UnitsUsed,
            reading.UnitPrice,
            reading.TotalAmount,
            reading.Notes,
            reading.CreatedAtUtc));
    }

    private static async Task<IResult> GetLatestMeterReadingsAsync(
        [FromRoute] Guid propertyId,
        [FromRoute] Guid roomId,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var latestElec = await db.MeterReadings
            .AsNoTracking()
            .Where(m => m.PropertyId == propertyId && m.RoomId == roomId && m.UtilityType == UtilityType.Electricity)
            .OrderByDescending(m => m.ReadingDate)
            .Select(m => (decimal?)m.CurrentReading)
            .FirstOrDefaultAsync(ct);

        var latestWater = await db.MeterReadings
            .AsNoTracking()
            .Where(m => m.PropertyId == propertyId && m.RoomId == roomId && m.UtilityType == UtilityType.Water)
            .OrderByDescending(m => m.ReadingDate)
            .Select(m => (decimal?)m.CurrentReading)
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new LatestRoomMeterReadingsDto(roomId, latestElec, latestWater));
    }

    // ── Monthly Invoice Handlers ──────────────────────────────────────────────
    private static async Task<IResult> GetInvoicesAsync(
        [FromRoute] Guid propertyId,
        [FromQuery] string? billingMonth,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var query = db.MonthlyInvoices
            .AsNoTracking()
            .Include(i => i.Agreement)
            .Include(i => i.Tenant)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .Where(i => i.PropertyId == propertyId);

        if (!string.IsNullOrWhiteSpace(billingMonth))
            query = query.Where(i => i.BillingMonth == billingMonth.Trim());

        var list = await query
            .OrderByDescending(i => i.CreatedAtUtc)
            .Select(i => MapToInvoiceDto(i))
            .ToListAsync(ct);

        return Results.Ok(list);
    }

    private static async Task<IResult> GenerateMonthlyInvoicesAsync(
        [FromRoute] Guid propertyId,
        [FromBody] GenerateMonthlyInvoicesRequest req,
        [FromServices] StayFlowDbContext db,
        [FromServices] ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.BillingMonth))
            return Results.BadRequest(new { message = "กรุณาระบุรอบบิล (เช่น '2026-11')" });

        var orgId = tenantContext.OrganizationId ?? Guid.Empty;

        // Fetch all active rental agreements
        var activeAgreements = await db.RentalAgreements
            .Include(a => a.Tenant)
            .Include(a => a.Room)
            .Where(a => a.PropertyId == propertyId && a.Status == AgreementStatus.Active)
            .ToListAsync(ct);

        int generatedCount = 0;
        int skippedCount = 0;
        var generatedInvoices = new List<MonthlyInvoiceDto>();

        foreach (var agreement in activeAgreements)
        {
            // Idempotency check: Skip if an invoice already exists for this agreement and billing month
            var exists = await db.MonthlyInvoices
                .AnyAsync(i => i.PropertyId == propertyId
                               && i.AgreementId == agreement.Id
                               && i.BillingMonth == req.BillingMonth.Trim()
                               && i.Status != InvoiceStatus.Cancelled, ct);

            if (exists)
            {
                skippedCount++;
                continue;
            }

            // Create new MonthlyInvoice
            var invoiceNum = $"INV-{req.BillingMonth.Replace("-", "")}-{agreement.Room?.RoomNumber ?? "000"}";
            var invoice = MonthlyInvoice.Create(
                orgId,
                propertyId,
                invoiceNum,
                agreement.Id,
                agreement.TenantId,
                agreement.RoomId,
                req.BillingMonth.Trim(),
                req.DueDate);

            // 1. Add Monthly Rent line item
            invoice.AddItem(
                InvoiceItemType.Rent,
                $"ค่าเช่าห้องพักประจำเดือน {req.BillingMonth} (ห้อง {agreement.Room?.RoomNumber})",
                agreement.MonthlyRent,
                agreement.MonthlyRent,
                1);

            // 2. Lookup Electricity meter reading for this month if available
            var elecReading = await db.MeterReadings
                .FirstOrDefaultAsync(m => m.PropertyId == propertyId
                                          && m.RoomId == agreement.RoomId
                                          && m.UtilityType == UtilityType.Electricity
                                          && m.BillingMonth == req.BillingMonth.Trim(), ct);

            if (elecReading != null && elecReading.TotalAmount > 0)
            {
                invoice.AddItem(
                    InvoiceItemType.Electricity,
                    $"ค่าไฟฟ้า ({elecReading.PreviousReading:N0} - {elecReading.CurrentReading:N0} = {elecReading.UnitsUsed:N0} หน่วย @ {elecReading.UnitPrice:N2} บ.)",
                    elecReading.TotalAmount,
                    elecReading.UnitPrice,
                    elecReading.UnitsUsed);
            }

            // 3. Lookup Water meter reading for this month if available
            var waterReading = await db.MeterReadings
                .FirstOrDefaultAsync(m => m.PropertyId == propertyId
                                          && m.RoomId == agreement.RoomId
                                          && m.UtilityType == UtilityType.Water
                                          && m.BillingMonth == req.BillingMonth.Trim(), ct);

            if (waterReading != null && waterReading.TotalAmount > 0)
            {
                invoice.AddItem(
                    InvoiceItemType.Water,
                    $"ค่าน้ำประปา ({waterReading.PreviousReading:N0} - {waterReading.CurrentReading:N0} = {waterReading.UnitsUsed:N0} หน่วย @ {waterReading.UnitPrice:N2} บ.)",
                    waterReading.TotalAmount,
                    waterReading.UnitPrice,
                    waterReading.UnitsUsed);
            }

            // Automatically issue invoice
            invoice.Issue();

            db.MonthlyInvoices.Add(invoice);
            generatedCount++;

            // Explicitly track items in context
            foreach (var item in invoice.Items)
            {
                db.MonthlyInvoiceItems.Add(item);
            }

            generatedInvoices.Add(MapToInvoiceDto(invoice, agreement.AgreementNumber, agreement.Tenant?.FullName, agreement.Room?.RoomNumber));
        }

        if (generatedCount > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return Results.Ok(new GenerateInvoicesSummaryDto(
            activeAgreements.Count,
            generatedCount,
            skippedCount,
            generatedInvoices));
    }

    private static async Task<IResult> GetInvoiceByIdAsync(
        [FromRoute] Guid id,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var inv = await db.MonthlyInvoices
            .AsNoTracking()
            .Include(i => i.Agreement)
            .Include(i => i.Tenant)
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (inv == null) return Results.NotFound();

        var room = await db.Rooms.AsNoTracking().FirstOrDefaultAsync(r => r.Id == inv.RoomId, ct);

        return Results.Ok(MapToInvoiceDto(inv, inv.Agreement?.AgreementNumber, inv.Tenant?.FullName, room?.RoomNumber));
    }

    private static async Task<IResult> AddInvoiceItemAsync(
        [FromRoute] Guid id,
        [FromBody] AddInvoiceItemRequest req,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var inv = await db.MonthlyInvoices
            .Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (inv == null) return Results.NotFound();

        if (!Enum.TryParse<InvoiceItemType>(req.ItemType, true, out var itemType))
            itemType = InvoiceItemType.Other;

        try
        {
            inv.AddItem(itemType, req.Description, req.Amount, req.UnitPrice, req.Quantity);
            var item = inv.Items.Last();
            db.MonthlyInvoiceItems.Add(item);

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "เพิ่มรายการในบิลเรียบร้อยแล้ว", totalAmount = inv.TotalAmount, balance = inv.Balance });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> IssueInvoiceAsync(
        [FromRoute] Guid id,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var inv = await db.MonthlyInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null) return Results.NotFound();

        try
        {
            inv.Issue();
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "ออกใบแจ้งหนี้เรียบร้อยแล้ว", status = inv.Status.ToString() });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> RecordInvoicePaymentAsync(
        [FromRoute] Guid id,
        [FromBody] RecordInvoicePaymentRequest req,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var inv = await db.MonthlyInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (inv == null) return Results.NotFound();

        if (!Enum.TryParse<PaymentMethod>(req.Method, true, out var method))
            method = PaymentMethod.BankTransfer;

        try
        {
            inv.RecordPayment(req.Amount, method, req.ReferenceNumber, req.Notes);
            var paymentRecord = inv.Payments.Last();
            db.InvoicePaymentRecords.Add(paymentRecord);

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                message = "บันทึกการชำระเงินเรียบร้อยแล้ว",
                paidAmount = inv.PaidAmount,
                balance = inv.Balance,
                status = inv.Status.ToString()
            });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> CancelInvoiceAsync(
        [FromRoute] Guid id,
        [FromBody] ReversePaymentRequest req,
        [FromServices] StayFlowDbContext db,
        CancellationToken ct)
    {
        var inv = await db.MonthlyInvoices.FirstOrDefaultAsync(i => i.Id == id, ct);
        if (inv == null) return Results.NotFound();

        try
        {
            inv.Cancel(req.Reason);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { message = "ยกเลิกใบแจ้งหนี้เรียบร้อยแล้ว", status = inv.Status.ToString() });
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    // ── Helper Mapping ────────────────────────────────────────────────────────
    private static MonthlyInvoiceDto MapToInvoiceDto(
        MonthlyInvoice i,
        string? agreementNumber = null,
        string? tenantName = null,
        string? roomNumber = null)
    {
        return new MonthlyInvoiceDto(
            i.Id,
            i.OrganizationId,
            i.PropertyId,
            i.InvoiceNumber,
            i.AgreementId,
            agreementNumber ?? i.Agreement?.AgreementNumber,
            i.TenantId,
            tenantName ?? i.Tenant?.FullName,
            i.RoomId,
            roomNumber,
            i.BillingMonth,
            i.DueDate,
            i.TotalAmount,
            i.PaidAmount,
            i.Balance,
            i.Status.ToString(),
            i.PaidAtUtc,
            i.CreatedAtUtc,
            i.Items.Select(item => new MonthlyInvoiceItemDto(
                item.Id,
                item.ItemType.ToString(),
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.Amount)).ToList(),
            i.Payments.Select(p => new InvoicePaymentRecordDto(
                p.Id,
                p.Amount,
                p.Method.ToString(),
                p.ReferenceNumber,
                p.Notes,
                p.CreatedAtUtc)).ToList()
        );
    }
}

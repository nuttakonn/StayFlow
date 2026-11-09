using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Notifications;
using StayFlow.BuildingBlocks.Email;
using StayFlow.BuildingBlocks.Jobs;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record EmailSettingsDto(
    Guid PropertyId,
    bool SendBookingConfirmation,
    bool SendDailyReport,
    string DailyReportRecipients,
    int DailyReportSendHourUtc);

public record UpdateEmailSettingsRequest(
    bool SendBookingConfirmation,
    bool SendDailyReport,
    string DailyReportRecipients,
    int DailyReportSendHourUtc);

// ── Endpoints ─────────────────────────────────────────────────────────────────

public static class EmailSettingsEndpoints
{
    public static void MapEmailSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/v1/properties/{propertyId:guid}/email-settings")
            .WithTags("Email Settings")
            .RequireAuthorization();

        auth.MapGet("/", GetEmailSettingsAsync);
        auth.MapPut("/", UpdateEmailSettingsAsync);

        // Manual trigger: send the daily report now for a specific date
        auth.MapPost("/send-daily-report", SendDailyReportNowAsync);
    }

    private static async Task<IResult> GetEmailSettingsAsync(
        Guid propertyId,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null || property.OrganizationId != tenant.OrganizationId.Value)
            return Results.NotFound();

        var settings = await db.EmailNotificationSettings
            .FirstOrDefaultAsync(s => s.PropertyId == propertyId);

        if (settings == null)
            return Results.Ok(new EmailSettingsDto(propertyId, true, false, "", 6));

        return Results.Ok(ToDto(settings));
    }

    private static async Task<IResult> UpdateEmailSettingsAsync(
        Guid propertyId,
        [FromBody] UpdateEmailSettingsRequest req,
        StayFlowDbContext db,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null || property.OrganizationId != tenant.OrganizationId.Value)
            return Results.NotFound();

        if (req.DailyReportSendHourUtc is < 0 or > 23)
            return Results.Problem("DailyReportSendHourUtc must be 0–23.", statusCode: 400);

        var settings = await db.EmailNotificationSettings
            .FirstOrDefaultAsync(s => s.PropertyId == propertyId);

        if (settings == null)
        {
            settings = EmailNotificationSettings.CreateDefault(tenant.OrganizationId.Value, propertyId);
            db.EmailNotificationSettings.Add(settings);
        }

        settings.Update(
            req.SendBookingConfirmation,
            req.SendDailyReport,
            req.DailyReportRecipients,
            req.DailyReportSendHourUtc);

        await db.SaveChangesAsync();
        return Results.Ok(ToDto(settings));
    }

    private static async Task<IResult> SendDailyReportNowAsync(
        Guid propertyId,
        [FromQuery] string? date,
        StayFlowDbContext db,
        IEmailService emailService,
        ITenantContext tenant)
    {
        if (!tenant.OrganizationId.HasValue) return Results.Unauthorized();

        var property = await db.Properties.FindAsync([propertyId]);
        if (property == null || property.OrganizationId != tenant.OrganizationId.Value)
            return Results.NotFound();

        var reportDate = DateOnly.TryParse(date, out var d)
            ? d
            : DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

        await DailyReportBackgroundService.SendDailyReportAsync(db, emailService, propertyId, reportDate);

        return Results.Ok(new { propertyId, reportDate, sent = true });
    }

    private static EmailSettingsDto ToDto(EmailNotificationSettings s) =>
        new(s.PropertyId, s.SendBookingConfirmation, s.SendDailyReport,
            s.DailyReportRecipients, s.DailyReportSendHourUtc);
}

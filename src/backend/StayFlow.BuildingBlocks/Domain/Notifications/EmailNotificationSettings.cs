using StayFlow.BuildingBlocks.Common;

namespace StayFlow.BuildingBlocks.Domain.Notifications;

/// <summary>
/// Per-property email notification preferences.
/// One record per property; created on first save.
/// </summary>
public class EmailNotificationSettings : PropertyEntity<Guid>
{
    public bool SendBookingConfirmation { get; private set; } = true;
    public bool SendDailyReport { get; private set; } = false;

    // Comma-separated list of recipient email addresses for the daily report
    public string DailyReportRecipients { get; private set; } = string.Empty;

    // UTC hour (0-23) at which the daily report is sent; default 06:00 UTC
    public int DailyReportSendHourUtc { get; private set; } = 6;

    private EmailNotificationSettings() { }

    public static EmailNotificationSettings CreateDefault(Guid organizationId, Guid propertyId) =>
        new()
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            PropertyId = propertyId
        };

    public void Update(
        bool sendBookingConfirmation,
        bool sendDailyReport,
        string dailyReportRecipients,
        int dailyReportSendHourUtc)
    {
        SendBookingConfirmation = sendBookingConfirmation;
        SendDailyReport = sendDailyReport;
        DailyReportRecipients = dailyReportRecipients.Trim();
        DailyReportSendHourUtc = Math.Clamp(dailyReportSendHourUtc, 0, 23);
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    public IReadOnlyList<string> GetRecipientList() =>
        DailyReportRecipients
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => e.Contains('@'))
            .ToList();
}

namespace StayFlow.BuildingBlocks.Email;

public class EmailOptions
{
    public const string SectionName = "Email";

    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool UseTls { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "noreply@stayflow.local";
    public string FromName { get; set; } = "StayFlow PMS";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SmtpHost) &&
        !string.IsNullOrWhiteSpace(FromAddress);
}

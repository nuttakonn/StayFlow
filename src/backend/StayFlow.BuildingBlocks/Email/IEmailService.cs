namespace StayFlow.BuildingBlocks.Email;

public record EmailMessage(
    string To,
    string Subject,
    string HtmlBody,
    string? TextBody = null,
    string? ReplyTo = null,
    IReadOnlyList<string>? Cc = null);

public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
    bool IsConfigured { get; }
}

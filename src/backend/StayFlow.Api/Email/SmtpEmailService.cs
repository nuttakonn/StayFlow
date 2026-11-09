using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using StayFlow.BuildingBlocks.Email;

namespace StayFlow.Api.Email;

public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailOptions _opts;
    private readonly ILogger<SmtpEmailService> _logger;

    public SmtpEmailService(IOptions<EmailOptions> opts, ILogger<SmtpEmailService> logger)
    {
        _opts = opts.Value;
        _logger = logger;
    }

    public bool IsConfigured => _opts.IsConfigured;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Email not configured — skipping send to {To}", message.To);
            return;
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_opts.FromName, _opts.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));

        if (!string.IsNullOrWhiteSpace(message.ReplyTo))
            mime.ReplyTo.Add(MailboxAddress.Parse(message.ReplyTo));

        foreach (var cc in message.Cc ?? [])
            mime.Cc.Add(MailboxAddress.Parse(cc));

        mime.Subject = message.Subject;

        var body = new BodyBuilder
        {
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody
        };
        mime.Body = body.ToMessageBody();

        using var client = new SmtpClient();
        try
        {
            var sslMode = _opts.UseTls ? SecureSocketOptions.StartTls : SecureSocketOptions.None;
            await client.ConnectAsync(_opts.SmtpHost!, _opts.SmtpPort, sslMode, ct);

            if (!string.IsNullOrWhiteSpace(_opts.Username))
                await client.AuthenticateAsync(_opts.Username, _opts.Password, ct);

            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(quit: true, ct);

            _logger.LogInformation("Email sent to {To} subject={Subject}", message.To, message.Subject);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email to {To}", message.To);
            throw;
        }
    }
}

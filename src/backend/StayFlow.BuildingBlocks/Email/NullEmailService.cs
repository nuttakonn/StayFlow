using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace StayFlow.BuildingBlocks.Email;

/// <summary>
/// No-op email service used when SMTP is not configured. Captures messages
/// in memory so integration tests can assert on them without an SMTP server.
/// </summary>
public sealed class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _logger;
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public NullEmailService(ILogger<NullEmailService> logger)
    {
        _logger = logger;
    }

    public bool IsConfigured => false;

    public IReadOnlyList<EmailMessage> SentMessages => _sent.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _sent.Enqueue(message);
        _logger.LogInformation("[NullEmail] To={To} Subject={Subject}", message.To, message.Subject);
        return Task.CompletedTask;
    }
}

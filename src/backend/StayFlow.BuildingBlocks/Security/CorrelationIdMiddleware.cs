using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace StayFlow.BuildingBlocks.Security;

public class CorrelationIdMiddleware
{
    private const string CorrelationIdHeader = "X-Correlation-Id";
    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(CorrelationIdHeader, out var existingId) && !string.IsNullOrWhiteSpace(existingId)
            ? existingId.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[CorrelationIdHeader] = correlationId;

        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId });
        await _next(context);
    }
}

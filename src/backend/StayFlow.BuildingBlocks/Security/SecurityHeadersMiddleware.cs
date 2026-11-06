using Microsoft.AspNetCore.Http;

namespace StayFlow.BuildingBlocks.Security;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // Only set CSP on non-swagger/openapi paths to avoid breaking the dev UI
            var path = context.Request.Path.Value ?? "";
            if (!path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
                && !path.StartsWith("/openapi", StringComparison.OrdinalIgnoreCase))
            {
                headers["Content-Security-Policy"] = "default-src 'self'; frame-ancestors 'none'";
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

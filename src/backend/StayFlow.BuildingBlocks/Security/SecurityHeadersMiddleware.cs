using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace StayFlow.BuildingBlocks.Security;

public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _isProduction;

    public SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment env)
    {
        _next = next;
        _isProduction = env.IsProduction();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            var path = context.Request.Path.Value ?? "";

            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["X-XSS-Protection"] = "1; mode=block";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // HSTS: tell browsers to use HTTPS exclusively for one year (production only)
            if (_isProduction)
            {
                headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
            }

            // No-store on auth endpoints — prevents tokens from being cached by proxies or browsers
            if (path.StartsWith("/api/v1/auth", StringComparison.OrdinalIgnoreCase))
            {
                headers["Cache-Control"] = "no-store";
                headers["Pragma"] = "no-cache";
            }

            // Only set CSP on non-swagger/openapi paths to avoid breaking the dev UI
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

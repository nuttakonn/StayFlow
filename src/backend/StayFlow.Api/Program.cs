using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StayFlow.Api;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Branding;
using StayFlow.BuildingBlocks.ChannelManager;
using StayFlow.BuildingBlocks.Demo;
using StayFlow.BuildingBlocks.Deployment;
using StayFlow.BuildingBlocks.Entitlements;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;
using StayFlow.BuildingBlocks.Security;

var builder = WebApplication.CreateBuilder(args);

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Options
builder.Services.Configure<DeploymentOptions>(
    builder.Configuration.GetSection(DeploymentOptions.SectionName));
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

// Database Configuration
var defaultConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var dbProvider = builder.Configuration.GetValue<string>("DatabaseProvider") ?? "PostgreSQL";

if (dbProvider.Equals("InMemory", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(defaultConnectionString))
{
    builder.Services.AddDbContext<StayFlowDbContext>(options =>
    {
        options.UseInMemoryDatabase("StayFlowInMemoryDb");
    });
}
else
{
    builder.Services.AddDbContext<StayFlowDbContext>(options =>
    {
        options.UseNpgsql(defaultConnectionString,
            b => b.MigrationsAssembly("StayFlow.Api"));
    });
}

// Security & Domain Services
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IBrandingResolver, BrandingResolver>();
builder.Services.AddScoped<IEntitlementService, EntitlementService>();

// Channel Manager Providers (one implementation per OTA channel manager)
builder.Services.AddHttpClient();
builder.Services.AddTransient<IChannelManagerProvider, StaahChannelManagerProvider>();
builder.Services.AddTransient<IChannelManagerProvider, SiteMinderChannelManagerProvider>();
builder.Services.AddScoped<IChannelAvailabilitySync, ChannelAvailabilitySync>();

// JWT Authentication
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    var key = Encoding.UTF8.GetBytes(jwtOptions.SecretKey);
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtOptions.Issuer,
        ValidateAudience = true,
        ValidAudience = jwtOptions.Audience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});

builder.Services.AddAuthorization();

// JSON Options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

// Services
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Health checks: liveness (always up) + readiness (checks DB)
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: ["live"])
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// Rate limiting: fixed window on auth and sensitive endpoints.
// Limits are configurable so that test and on-premise deployments (where all
// staff share one egress IP) can be tuned without a code change.
var authPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:AuthPermitLimit") ?? 20;
var apiPermitLimit = builder.Configuration.GetValue<int?>("RateLimiting:ApiPermitLimit") ?? 300;

builder.Services.AddRateLimiter(options =>
{
    // Both policies are partitioned per caller. A single shared bucket would
    // let one noisy client lock out every other user of the deployment.
    options.AddPolicy("auth", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPartitionKey.ForClient(httpContext.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0
            }));

    options.AddPolicy("api", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            RateLimitPartitionKey.ForUserOrClient(
                ResolveUserId(httpContext), httpContext.Connection.RemoteIpAddress),
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = apiPermitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            }));

    options.RejectionStatusCode = 429;
});

static Guid? ResolveUserId(HttpContext context)
{
    if (context.User.Identity?.IsAuthenticated != true) return null;
    var raw = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? context.User.FindFirst("sub")?.Value;
    return Guid.TryParse(raw, out var id) ? id : null;
}

// Reverse-proxy awareness. With TLS terminated by nginx/Caddy in front of
// Kestrel, RemoteIpAddress is the proxy's address for every request, which
// would silently collapse the per-IP partitioning above back into one bucket.
// X-Forwarded-For is only honoured from explicitly configured proxies —
// trusting it unconditionally would let any caller forge a partition key and
// evade the limit entirely.
var knownProxies = builder.Configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
var knownNetworks = builder.Configuration.GetSection("ReverseProxy:KnownNetworks").Get<string[]>() ?? [];
var trustForwardedHeaders = knownProxies.Length > 0 || knownNetworks.Length > 0;

if (trustForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (var proxy in knownProxies)
        {
            if (IPAddress.TryParse(proxy, out var address))
            {
                options.KnownProxies.Add(address);
            }
        }

        foreach (var network in knownNetworks)
        {
            var parts = network.Split('/', 2);
            if (parts.Length == 2
                && IPAddress.TryParse(parts[0], out var prefix)
                && int.TryParse(parts[1], out var prefixLength))
            {
                // System.Net.IPNetwork is the BCL type expected by KnownIPNetworks
                // in ASP.NET Core 10 (the older HttpOverrides.IPNetwork is obsolete).
                options.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
            }
        }
    });
}

// CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? ["http://localhost:5173", "http://localhost:3000"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// Apply pending migrations (PostgreSQL) or ensure schema exists (InMemory).
// MigrateAsync is idempotent and safe to call on every startup.
using (var seedScope = app.Services.CreateScope())
{
    var seedDbContext = seedScope.ServiceProvider.GetRequiredService<StayFlowDbContext>();
    if (seedDbContext.Database.IsRelational())
        await seedDbContext.Database.MigrateAsync();
    else
        await seedDbContext.Database.EnsureCreatedAsync();

    await SubscriptionPlanSeeder.EnsureDefaultPlansAsync(seedDbContext);
    var seedHasher = seedScope.ServiceProvider.GetRequiredService<IPasswordHasher>();
    await DemoDataSeeder.EnsureDemoDataAsync(seedDbContext, seedHasher);
}

// Pipeline
// Must be first: populates Connection.RemoteIpAddress from X-Forwarded-For
// before any middleware that reads it (rate limiter, tenant context, audit).
// Only active when KnownProxies / KnownNetworks are configured — the default
// is a no-op so a bare Kestrel setup doesn't blindly trust forwarded headers.
if (trustForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("DefaultCorsPolicy");
app.UseStaticFiles(); // serves wwwroot/ including uploaded logos
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseRateLimiter();

// Swagger UI & OpenAPI Specification
app.MapOpenApi();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/openapi/v1.json", "StayFlow PMS API v1");
    options.RoutePrefix = "swagger";
    options.DocumentTitle = "StayFlow PMS - API Documentation";
});

app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();

// Health probes (Phase 8 — Production Hardening)
// /healthz/live  → liveness: confirms process is running
// /healthz/ready → readiness: confirms DB reachable
app.MapHealthChecks("/healthz/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            timestamp = DateTimeOffset.UtcNow,
            service = "StayFlow.Api",
            version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0"
        });
        await context.Response.WriteAsync(result);
    }
}).WithName("LivenessProbe").WithTags("System");

app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs = e.Value.Duration.TotalMilliseconds
        });
        var result = System.Text.Json.JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            timestamp = DateTimeOffset.UtcNow,
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks
        });
        await context.Response.WriteAsync(result);
    }
}).WithName("ReadinessProbe").WithTags("System");

// Legacy health endpoint kept for backward compatibility
app.MapGet("/healthz", (IOptions<DeploymentOptions> deploymentOptions, IHostEnvironment env) =>
{
    var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";
    return Results.Ok(new
    {
        status = "Healthy",
        timestamp = DateTimeOffset.UtcNow,
        service = "StayFlow.Api",
        version = assemblyVersion,
        environment = env.EnvironmentName,
        deploymentMode = deploymentOptions.Value.Mode.ToString()
    });
})
.WithName("HealthCheck")
.WithTags("System");

app.MapGet("/", (IOptions<DeploymentOptions> deploymentOptions) =>
{
    return Results.Ok(new
    {
        name = "StayFlow PMS API",
        version = "1.0.0",
        swaggerUi = "/swagger",
        openApiSpec = "/openapi/v1.json",
        deploymentMode = deploymentOptions.Value.Mode.ToString()
    });
})
.WithName("ApiInfo")
.WithTags("System");

// Module Route Groups
var apiV1 = app.MapGroup("/api/v1");

apiV1.MapGroup("/auth")
    .WithTags("Authentication")
    .RequireRateLimiting("auth")
    .MapAuthEndpoints();

apiV1.MapGroup("/organizations")
    .WithTags("Organizations")
    .RequireAuthorization()
    .MapOrganizationEndpoints();

apiV1.MapGroup("")
    .WithTags("Subscriptions")
    .RequireAuthorization()
    .MapSubscriptionEndpoints();

apiV1.MapGroup("/properties")
    .WithTags("Properties")
    .RequireAuthorization()
    .MapPropertyEndpoints();

apiV1.MapGroup("")
    .WithTags("Inventory & Rooms")
    .RequireAuthorization()
    .MapRoomEndpoints();

apiV1.MapGroup("/branding")
    .WithTags("Branding")
    .MapBrandingEndpoints();

apiV1.MapGroup("/audit")
    .WithTags("Audit")
    .RequireAuthorization()
    .MapAuditEndpoints();

// Phase 3: Guests & Reservations
apiV1.MapGroup("/guests")
    .WithTags("Guests")
    .RequireAuthorization()
    .MapGuestEndpoints();

apiV1.MapGroup("/reservations")
    .WithTags("Reservations")
    .RequireAuthorization()
    .MapReservationEndpoints();

// Phase 3: Availability (nested under /properties)
apiV1.MapGroup("/properties")
    .WithTags("Availability")
    .RequireAuthorization()
    .MapAvailabilityEndpoints();

// Phase 4: Stay Operations & Front Desk
apiV1.MapGroup("/properties/{propertyId:guid}/operations")
    .WithTags("Stay Operations")
    .RequireAuthorization()
    .MapStayOperationsEndpoints();

// Phase 5: Billing, Folios & Payments
apiV1.MapGroup("")
    .WithTags("Billing & Folios")
    .RequireAuthorization()
    .MapBillingEndpoints();

// Phase 5: Operational & Financial Reporting
apiV1.MapGroup("/properties/{propertyId:guid}/reports")
    .WithTags("Reporting")
    .RequireAuthorization()
    .MapReportingEndpoints();

// Operational Dashboard
apiV1.MapGroup("/properties/{propertyId:guid}/dashboard")
    .WithTags("Dashboard")
    .RequireAuthorization()
    .MapDashboardEndpoints();

// Maintenance requests
apiV1.MapGroup("/properties/{propertyId:guid}/maintenance")
    .WithTags("Maintenance")
    .RequireAuthorization()
    .MapMaintenanceEndpoints();

// Phase 6: Apartments & Monthly Rentals
app.MapApartmentEndpoints();

// M9: Channel Manager (STAAH / SiteMinder)
app.MapChannelManagerEndpoints();

// M10: Rate Plans, Coupons, Group Bookings
app.MapRatePlanEndpoints();
app.MapCouponEndpoints();
app.MapGroupBookingEndpoints();
app.MapServiceItemEndpoints();
app.MapPackageEndpoints();
app.MapQuotationEndpoints();

app.Run();

// Make Program accessible for WebApplicationFactory in integration tests
public partial class Program { }

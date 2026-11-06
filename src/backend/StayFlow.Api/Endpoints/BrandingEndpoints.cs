using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Branding;
using StayFlow.BuildingBlocks.Domain.Branding;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record UpdateBrandingRequest(
    Guid? PropertyId,
    string DisplayName,
    string? LogoUrl,
    string? FaviconUrl,
    string? PrimaryColor,
    string? SecondaryColor,
    string? LoginTitle,
    string? LoginWelcomeText,
    string? FooterText,
    string? SupportEmail,
    string? SupportPhone,
    bool ShowVendorAttribution
);

public record UploadLogoResponse(string Url);

public static class BrandingEndpoints
{
    // Allowed image magic bytes: PNG, JPEG, WebP
    private static readonly (byte[] Magic, string Ext)[] AllowedFormats =
    [
        (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, ".png"),           // PNG
        (new byte[] { 0xFF, 0xD8, 0xFF }, ".jpg"),                   // JPEG
        (new byte[] { 0x52, 0x49, 0x46, 0x46 }, ".webp"),           // WebP (RIFF header)
    ];

    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB

    public static RouteGroupBuilder MapBrandingEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetEffectiveBrandingAsync)
            .WithName("GetEffectiveBranding")
            .WithSummary("Get effective branding (Property override -> Organization -> Platform default)")
            .Produces<BrandingDto>(StatusCodes.Status200OK)
            .AllowAnonymous();

        group.MapPut("/", UpdateBrandingAsync)
            .WithName("UpdateBranding")
            .WithSummary("Configure organization or property-level branding (Requires active Organization)")
            .Produces<BrandingDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        group.MapPost("/upload-logo", UploadLogoAsync)
            .WithName("UploadLogo")
            .WithSummary("Upload a logo image (PNG/JPEG/WebP, max 2 MB). Returns the URL to use as LogoUrl.")
            .Produces<UploadLogoResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .DisableAntiforgery()
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetEffectiveBrandingAsync(
        [FromQuery] Guid? organizationId,
        [FromQuery] Guid? propertyId,
        IBrandingResolver brandingResolver,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        var targetOrgId = organizationId ?? tenantContext.OrganizationId;
        var targetPropId = propertyId ?? tenantContext.PropertyId;

        var effectiveBranding = await brandingResolver.ResolveEffectiveBrandingAsync(targetOrgId, targetPropId, ct);
        return Results.Ok(effectiveBranding);
    }

    private static async Task<IResult> UpdateBrandingAsync(
        [FromBody] UpdateBrandingRequest request,
        StayFlowDbContext dbContext,
        IBrandingResolver brandingResolver,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
        {
            return Results.Problem("Active organization context is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var orgId = tenantContext.OrganizationId.Value;

        // If propertyId specified, verify it belongs to this organization
        if (request.PropertyId.HasValue)
        {
            var propExists = await dbContext.Properties
                .IgnoreQueryFilters()
                .AnyAsync(p => p.Id == request.PropertyId.Value && p.OrganizationId == orgId, ct);

            if (!propExists)
            {
                return Results.Problem("Invalid Property ID for this organization.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var existing = await dbContext.BrandingSettings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.OrganizationId == orgId && b.PropertyId == request.PropertyId, ct);

        if (existing == null)
        {
            existing = BrandingSettings.Create(
                organizationId: orgId,
                propertyId: request.PropertyId,
                displayName: request.DisplayName,
                logoUrl: request.LogoUrl,
                faviconUrl: request.FaviconUrl,
                primaryColor: request.PrimaryColor,
                secondaryColor: request.SecondaryColor,
                loginTitle: request.LoginTitle,
                loginWelcomeText: request.LoginWelcomeText,
                footerText: request.FooterText,
                supportEmail: request.SupportEmail,
                supportPhone: request.SupportPhone,
                showVendorAttribution: request.ShowVendorAttribution
            );
            dbContext.BrandingSettings.Add(existing);
        }
        else
        {
            existing.Update(
                displayName: request.DisplayName,
                logoUrl: request.LogoUrl,
                faviconUrl: request.FaviconUrl,
                primaryColor: request.PrimaryColor,
                secondaryColor: request.SecondaryColor,
                loginTitle: request.LoginTitle,
                loginWelcomeText: request.LoginWelcomeText,
                footerText: request.FooterText,
                supportEmail: request.SupportEmail,
                supportPhone: request.SupportPhone,
                showVendorAttribution: request.ShowVendorAttribution
            );
        }

        await dbContext.SaveChangesAsync(ct);

        var updated = await brandingResolver.ResolveEffectiveBrandingAsync(orgId, request.PropertyId, ct);
        return Results.Ok(updated);
    }

    private static async Task<IResult> UploadLogoAsync(
        IFormFile? file,
        ITenantContext tenantContext,
        IWebHostEnvironment env,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Problem("Active organization context is required.", statusCode: StatusCodes.Status400BadRequest);

        if (file == null || file.Length == 0)
            return Results.Problem("No file uploaded.", statusCode: StatusCodes.Status400BadRequest);

        if (file.Length > MaxFileSizeBytes)
            return Results.Problem("File size exceeds the 2 MB limit.", statusCode: StatusCodes.Status400BadRequest);

        // Read first 12 bytes to detect format by magic bytes (not trusting ContentType/extension)
        var header = new byte[12];
        int headerRead;
        await using (var stream = file.OpenReadStream())
        {
            headerRead = await stream.ReadAsync(header.AsMemory(0, header.Length), ct);
        }

        string? detectedExt = null;
        foreach (var (magic, ext) in AllowedFormats)
        {
            if (headerRead >= magic.Length && header.Take(magic.Length).SequenceEqual(magic))
            {
                // For WebP, also verify "WEBP" at bytes 8-11
                if (ext == ".webp")
                {
                    var webpSig = "WEBP"u8.ToArray();
                    if (headerRead >= 12 && header[8..12].SequenceEqual(webpSig))
                    {
                        detectedExt = ext;
                        break;
                    }
                }
                else
                {
                    detectedExt = ext;
                    break;
                }
            }
        }

        if (detectedExt == null)
            return Results.Problem("Unsupported image format. Only PNG, JPEG, and WebP are allowed.", statusCode: StatusCodes.Status400BadRequest);

        // Generate a server-side filename scoped to the organization (prevents path traversal)
        var orgId = tenantContext.OrganizationId.Value;
        var fileName = $"logo_{orgId}_{Guid.NewGuid():N}{detectedExt}";

        // Store under wwwroot/uploads/logos — create if needed
        var uploadsDir = Path.Combine(env.WebRootPath, "uploads", "logos");
        Directory.CreateDirectory(uploadsDir);

        var filePath = Path.Combine(uploadsDir, fileName);

        await using (var dest = File.Create(filePath))
        await using (var src = file.OpenReadStream())
        {
            await src.CopyToAsync(dest, ct);
        }

        var request = httpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";
        var logoUrl = $"{baseUrl}/uploads/logos/{fileName}";

        return Results.Ok(new UploadLogoResponse(logoUrl));
    }
}

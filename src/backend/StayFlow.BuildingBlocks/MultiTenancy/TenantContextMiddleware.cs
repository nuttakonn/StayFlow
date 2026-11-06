using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Authorization;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.MultiTenancy;

public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, StayFlowDbContext dbContext)
    {
        if (context.User.Identity?.IsAuthenticated == true && tenantContext is TenantContext mutableContext)
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User.FindFirst("sub")?.Value;

            Guid? userId = Guid.TryParse(userIdStr, out var parsedUserId) ? parsedUserId : null;
            var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value
                ?? context.User.FindFirst("email")?.Value;

            Guid? orgId = null;

            // Header takes precedence if provided and user belongs to organization
            if (context.Request.Headers.TryGetValue("X-Organization-Id", out var orgHeader) 
                && Guid.TryParse(orgHeader, out var parsedOrgId) && userId.HasValue)
            {
                // Verify user actually belongs to this organization
                var membership = await dbContext.OrganizationMemberships
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.OrganizationId == parsedOrgId && m.UserId == userId.Value);

                if (membership != null)
                {
                    orgId = parsedOrgId;
                }
            }

            // Otherwise, fall back to token claim
            if (!orgId.HasValue)
            {
                var orgClaim = context.User.FindFirst("org_id")?.Value;
                if (Guid.TryParse(orgClaim, out var tokenOrgId))
                {
                    orgId = tokenOrgId;
                }
            }

            // Resolve org-level membership and base permissions
            var permissions = context.User.FindAll("permission").Select(c => c.Value).ToList();
            OrganizationRole? orgRole = null;

            if (orgId.HasValue && userId.HasValue)
            {
                var currentMembership = await dbContext.OrganizationMemberships
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.OrganizationId == orgId.Value && m.UserId == userId.Value);

                if (currentMembership != null)
                {
                    orgRole = currentMembership.Role;
                    permissions = Permissions.GetPermissionsForRole(currentMembership.Role).ToList();
                }
            }

            // Extract Property Id from header; verify access
            Guid? propertyId = null;
            if (context.Request.Headers.TryGetValue("X-Property-Id", out var propHeader)
                && Guid.TryParse(propHeader, out var parsedPropId) && orgId.HasValue)
            {
                var propExists = await dbContext.Properties
                    .AsNoTracking()
                    .IgnoreQueryFilters()
                    .AnyAsync(p => p.Id == parsedPropId && p.OrganizationId == orgId.Value && p.IsActive);

                if (propExists)
                {
                    propertyId = parsedPropId;

                    // Owner/Admin have implicit full access to every property.
                    // For narrower roles, resolve the property-scoped role (if any) and
                    // use whichever grants more permissions.
                    if (orgRole.HasValue
                        && orgRole.Value is not (OrganizationRole.Owner or OrganizationRole.Admin)
                        && userId.HasValue)
                    {
                        var orgMembership = await dbContext.OrganizationMemberships
                            .AsNoTracking()
                            .FirstOrDefaultAsync(m => m.OrganizationId == orgId.Value && m.UserId == userId.Value);

                        if (orgMembership != null)
                        {
                            var propMembership = await dbContext.PropertyMemberships
                                .AsNoTracking()
                                .FirstOrDefaultAsync(pm =>
                                    pm.OrganizationMembershipId == orgMembership.Id &&
                                    pm.PropertyId == parsedPropId);

                            if (propMembership != null)
                            {
                                // Use the more permissive of the two roles
                                var effectiveRole = propMembership.Role < orgRole.Value
                                    ? propMembership.Role
                                    : orgRole.Value;
                                permissions = Permissions.GetPermissionsForRole(effectiveRole).ToList();
                            }
                            else
                            {
                                // No property assignment → no property-scoped permissions
                                propertyId = null;
                                permissions = [];
                            }
                        }
                    }
                }
            }

            mutableContext.SetContext(orgId, propertyId, userId, userEmail, permissions);
        }

        await _next(context);
    }
}

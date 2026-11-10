using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Identity;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.Deployment;
using StayFlow.BuildingBlocks.Entitlements;
using StayFlow.BuildingBlocks.MultiTenancy;
using Microsoft.Extensions.Options;
using StayFlow.BuildingBlocks.Persistence;
using StayFlow.BuildingBlocks.Security;

namespace StayFlow.Api.Endpoints;

public record CreateOrganizationRequest(
    string Name,
    string? Slug
);

public record AddMemberRequest(
    string Email,
    string FullName,
    OrganizationRole Role,
    string? Password = null
);

public record ChangeMemberRoleRequest(OrganizationRole NewRole);

public record ResetMemberPasswordRequest(string NewPassword);

public record OrganizationDetailDto(
    Guid Id,
    string Name,
    string Slug,
    bool IsActive,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<OrganizationMemberDto> Members
);

public record OrganizationMemberDto(
    Guid MembershipId,
    Guid UserId,
    string Email,
    string FullName,
    string Role,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyCollection<PropertyAssignmentDto>? PropertyAssignments = null
);

public record PropertyAssignmentDto(
    Guid PropertyMembershipId,
    Guid PropertyId,
    string PropertyName,
    string Role
);

public record AssignPropertyRequest(Guid PropertyId, OrganizationRole Role);
public record UpdatePropertyAssignmentRequest(OrganizationRole Role);

public static class OrganizationEndpoints
{
    public static RouteGroupBuilder MapOrganizationEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetUserOrganizationsAsync)
            .WithName("GetUserOrganizations")
            .WithSummary("List all organizations the current user belongs to")
            .Produces<List<UserOrganizationDto>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateOrganizationAsync)
            .WithName("CreateOrganization")
            .WithSummary("Create a new organization (caller becomes Owner)")
            .Produces<OrganizationDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetOrganizationByIdAsync)
            .WithName("GetOrganizationById")
            .WithSummary("Get organization details if the user is a member")
            .Produces<OrganizationDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/{id:guid}/members", AddMemberAsync)
            .WithName("AddOrganizationMember")
            .WithSummary("Add or invite a user to the organization (Requires Owner/Admin role)")
            .Produces<OrganizationMemberDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/{id:guid}/members/{memberId:guid}", ChangeMemberRoleAsync)
            .WithName("ChangeMemberRole")
            .WithSummary("Change a member's role (Requires Owner/Admin; Owner cannot be demoted by Admin)")
            .Produces<OrganizationMemberDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        group.MapPost("/{id:guid}/members/{memberId:guid}/reset-password", ResetMemberPasswordAsync)
            .WithName("ResetMemberPassword")
            .WithSummary("Reset a member's password (Owner/Admin only; cannot reset another Owner)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapDelete("/{id:guid}/members/{memberId:guid}", RemoveMemberAsync)
            .WithName("RemoveOrganizationMember")
            .WithSummary("Remove a member from the organization (Owner cannot be removed)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        // Property assignment endpoints
        group.MapPost("/{id:guid}/members/{memberId:guid}/properties", AssignPropertyAsync)
            .WithName("AssignMemberToProperty")
            .WithSummary("Grant a member access to a specific property with a role (Owner/Admin only)")
            .Produces<PropertyAssignmentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .RequireAuthorization();

        group.MapPatch("/{id:guid}/members/{memberId:guid}/properties/{propertyMembershipId:guid}", UpdatePropertyAssignmentAsync)
            .WithName("UpdatePropertyAssignment")
            .WithSummary("Update a member's role for a specific property (Owner/Admin only)")
            .Produces<PropertyAssignmentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapDelete("/{id:guid}/members/{memberId:guid}/properties/{propertyMembershipId:guid}", RemovePropertyAssignmentAsync)
            .WithName("RemovePropertyAssignment")
            .WithSummary("Revoke a member's access to a specific property (Owner/Admin only)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetUserOrganizationsAsync(
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var organizations = await dbContext.OrganizationMemberships
            .Where(m => m.UserId == tenantContext.UserId.Value)
            .Join(dbContext.Organizations.Where(o => o.IsActive),
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => new UserOrganizationDto(
                    o.Id,
                    o.Name,
                    o.Slug,
                    m.Role.ToString()))
            .ToListAsync(ct);

        return Results.Ok(organizations);
    }

    private static async Task<IResult> CreateOrganizationAsync(
        [FromBody] CreateOrganizationRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IOptions<DeploymentOptions> deploymentOptions,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("Organization name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var org = Organization.Create(request.Name, request.Slug);
        dbContext.Organizations.Add(org);

        var membership = OrganizationMembership.Create(org.Id, tenantContext.UserId.Value, OrganizationRole.Owner);
        dbContext.OrganizationMemberships.Add(membership);

        // SaaS organizations start on a trial of the default plan (On-Premise gets no
        // Subscription row; see docs/adr/0008). Shared with AuthEndpoints' registration
        // flows so every "create organization" path provisions identically.
        await SubscriptionProvisioning.StartTrialForNewOrganizationAsync(
            dbContext, deploymentOptions.Value, org.Id, tenantContext.UserId, ct);

        await dbContext.SaveChangesAsync(ct);

        var user = await dbContext.Users.FindAsync([tenantContext.UserId.Value], ct);

        var memberDto = new OrganizationMemberDto(
            membership.Id,
            tenantContext.UserId.Value,
            user?.Email ?? tenantContext.UserEmail ?? string.Empty,
            user?.FullName ?? "Owner",
            membership.Role.ToString(),
            membership.CreatedAtUtc
        );

        return Results.Created($"/api/v1/organizations/{org.Id}", new OrganizationDetailDto(
            org.Id,
            org.Name,
            org.Slug,
            org.IsActive,
            org.CreatedAtUtc,
            [memberDto]
        ));
    }

    private static async Task<IResult> GetOrganizationByIdAsync(
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        // Check if user is a member of this organization
        var userMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (userMembership == null)
        {
            return Results.Problem("Access denied to this organization.", statusCode: StatusCodes.Status403Forbidden);
        }

        var org = await dbContext.Organizations.FindAsync([id], ct);
        if (org == null) return Results.NotFound();

        var memberships = await dbContext.OrganizationMemberships
            .Where(m => m.OrganizationId == id)
            .Join(dbContext.Users,
                m => m.UserId,
                u => u.Id,
                (m, u) => new { Membership = m, User = u })
            .ToListAsync(ct);

        // Load property assignments for all members in one query
        var membershipIds = memberships.Select(x => x.Membership.Id).ToList();
        var allAssignments = await dbContext.PropertyMemberships
            .Where(pm => membershipIds.Contains(pm.OrganizationMembershipId))
            .Join(dbContext.Properties.IgnoreQueryFilters(),
                pm => pm.PropertyId,
                p => p.Id,
                (pm, p) => new { PropertyMembership = pm, Property = p })
            .ToListAsync(ct);

        var members = memberships.Select(x =>
        {
            var assignments = allAssignments
                .Where(a => a.PropertyMembership.OrganizationMembershipId == x.Membership.Id)
                .Select(a => new PropertyAssignmentDto(
                    a.PropertyMembership.Id,
                    a.Property.Id,
                    a.Property.Name,
                    a.PropertyMembership.Role.ToString()))
                .ToList();

            return new OrganizationMemberDto(
                x.Membership.Id,
                x.User.Id,
                x.User.Email,
                x.User.FullName,
                x.Membership.Role.ToString(),
                x.Membership.CreatedAtUtc,
                assignments);
        }).ToList();

        return Results.Ok(new OrganizationDetailDto(
            org.Id,
            org.Name,
            org.Slug,
            org.IsActive,
            org.CreatedAtUtc,
            members
        ));
    }

    private static async Task<IResult> AddMemberAsync(
        Guid id,
        [FromBody] AddMemberRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        // Verify caller is Owner or Admin in this organization
        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
        {
            return Results.Problem("Only organization Owners or Admins can add members.", statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.Problem("Email is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        // Check if user already exists or create provisioned user
        var targetUser = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (targetUser == null)
        {
            // Use admin-supplied password when provided; otherwise generate a random hash
            // (admin must share the password out-of-band; a reset flow can be added later)
            string passwordToHash;
            if (!string.IsNullOrWhiteSpace(request.Password))
            {
                if (request.Password.Length < 8 || !request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
                    return Results.Problem("Password must be at least 8 characters and contain at least one letter and one digit.", statusCode: StatusCodes.Status400BadRequest);
                passwordToHash = request.Password;
            }
            else
            {
                passwordToHash = Guid.NewGuid().ToString("N") + "Aa1";
            }
            var passHash = passwordHasher.HashPassword(passwordToHash);
            targetUser = User.Create(normalizedEmail, passHash, request.FullName);
            dbContext.Users.Add(targetUser);
            await dbContext.SaveChangesAsync(ct);
        }

        // Check if already a member
        var existingMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == targetUser.Id, ct);

        if (existingMembership != null)
        {
            return Results.Problem("User is already a member of this organization.", statusCode: StatusCodes.Status400BadRequest);
        }

        var newMembership = OrganizationMembership.Create(id, targetUser.Id, request.Role);
        dbContext.OrganizationMemberships.Add(newMembership);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/organizations/{id}/members/{newMembership.Id}", new OrganizationMemberDto(
            newMembership.Id,
            targetUser.Id,
            targetUser.Email,
            targetUser.FullName,
            newMembership.Role.ToString(),
            newMembership.CreatedAtUtc
        ));
    }

    private static async Task<IResult> ChangeMemberRoleAsync(
        Guid id,
        Guid memberId,
        [FromBody] ChangeMemberRoleRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can change member roles.", statusCode: StatusCodes.Status403Forbidden);

        var target = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == id, ct);

        if (target == null) return Results.NotFound();

        // Admin cannot promote/demote Owner
        if (target.Role == OrganizationRole.Owner && callerMembership.Role != OrganizationRole.Owner)
            return Results.Problem("Only an Owner can change another Owner's role.", statusCode: StatusCodes.Status403Forbidden);

        // Cannot set someone as Owner unless caller is Owner
        if (request.NewRole == OrganizationRole.Owner && callerMembership.Role != OrganizationRole.Owner)
            return Results.Problem("Only an Owner can grant the Owner role.", statusCode: StatusCodes.Status403Forbidden);

        target.UpdateRole(request.NewRole);
        await dbContext.SaveChangesAsync(ct);

        var targetUser = await dbContext.Users.FindAsync([target.UserId], ct);
        return Results.Ok(new OrganizationMemberDto(
            target.Id, target.UserId, targetUser?.Email ?? "", targetUser?.FullName ?? "",
            target.Role.ToString(), target.CreatedAtUtc));
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid id,
        Guid memberId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can remove members.", statusCode: StatusCodes.Status403Forbidden);

        var target = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == id, ct);

        if (target == null) return Results.NotFound();

        if (target.Role == OrganizationRole.Owner)
            return Results.Problem("The Owner cannot be removed from the organization.", statusCode: StatusCodes.Status400BadRequest);

        // Cannot remove yourself
        if (target.UserId == tenantContext.UserId.Value)
            return Results.Problem("You cannot remove yourself from the organization.", statusCode: StatusCodes.Status400BadRequest);

        dbContext.OrganizationMemberships.Remove(target);
        await dbContext.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> AssignPropertyAsync(
        Guid id,
        Guid memberId,
        [FromBody] AssignPropertyRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can assign property access.", statusCode: StatusCodes.Status403Forbidden);

        var target = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == id, ct);
        if (target == null)
            return Results.Problem($"Member membership {memberId} not found in org {id}.", statusCode: StatusCodes.Status404NotFound);

        // Owner/Admin have implicit access — no assignment needed
        if (target.Role is OrganizationRole.Owner or OrganizationRole.Admin)
            return Results.Problem("Owner and Admin roles have implicit access to all properties.", statusCode: StatusCodes.Status400BadRequest);

        // Verify property belongs to this org
        var property = await dbContext.Properties
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == request.PropertyId && p.OrganizationId == id, ct);
        if (property == null)
            return Results.Problem($"Property {request.PropertyId} not found in org {id}.", statusCode: StatusCodes.Status404NotFound);

        var existing = await dbContext.PropertyMemberships
            .FirstOrDefaultAsync(pm => pm.OrganizationMembershipId == memberId && pm.PropertyId == request.PropertyId, ct);
        if (existing != null)
            return Results.Problem("Member already has access to this property.", statusCode: StatusCodes.Status400BadRequest);

        var pm = PropertyMembership.Create(memberId, id, request.PropertyId, request.Role);
        dbContext.PropertyMemberships.Add(pm);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/v1/organizations/{id}/members/{memberId}/properties/{pm.Id}",
            new PropertyAssignmentDto(pm.Id, property.Id, property.Name, pm.Role.ToString()));
    }

    private static async Task<IResult> UpdatePropertyAssignmentAsync(
        Guid id,
        Guid memberId,
        Guid propertyMembershipId,
        [FromBody] UpdatePropertyAssignmentRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can update property assignments.", statusCode: StatusCodes.Status403Forbidden);

        var pm = await dbContext.PropertyMemberships
            .FirstOrDefaultAsync(x => x.Id == propertyMembershipId && x.OrganizationMembershipId == memberId && x.OrganizationId == id, ct);
        if (pm == null) return Results.NotFound();

        pm.UpdateRole(request.Role);
        await dbContext.SaveChangesAsync(ct);

        var property = await dbContext.Properties.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == pm.PropertyId, ct);

        return Results.Ok(new PropertyAssignmentDto(pm.Id, pm.PropertyId, property?.Name ?? "", pm.Role.ToString()));
    }

    private static async Task<IResult> RemovePropertyAssignmentAsync(
        Guid id,
        Guid memberId,
        Guid propertyMembershipId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can remove property assignments.", statusCode: StatusCodes.Status403Forbidden);

        var pm = await dbContext.PropertyMemberships
            .FirstOrDefaultAsync(x => x.Id == propertyMembershipId && x.OrganizationMembershipId == memberId && x.OrganizationId == id, ct);
        if (pm == null) return Results.NotFound();

        dbContext.PropertyMemberships.Remove(pm);
        await dbContext.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ResetMemberPasswordAsync(
        Guid id,
        Guid memberId,
        [FromBody] ResetMemberPasswordRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue) return Results.Unauthorized();

        var callerMembership = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == id && m.UserId == tenantContext.UserId.Value, ct);

        if (callerMembership == null || callerMembership.Role is not (OrganizationRole.Owner or OrganizationRole.Admin))
            return Results.Problem("Only Owners or Admins can reset member passwords.", statusCode: StatusCodes.Status403Forbidden);

        var target = await dbContext.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.Id == memberId && m.OrganizationId == id, ct);

        if (target == null) return Results.NotFound();

        // Admin cannot reset Owner's password
        if (target.Role == OrganizationRole.Owner && callerMembership.Role != OrganizationRole.Owner)
            return Results.Problem("Only an Owner can reset another Owner's password.", statusCode: StatusCodes.Status403Forbidden);

        if (string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < 8 ||
            !request.NewPassword.Any(char.IsLetter) ||
            !request.NewPassword.Any(char.IsDigit))
            return Results.Problem("Password must be at least 8 characters and contain at least one letter and one digit.", statusCode: StatusCodes.Status400BadRequest);

        var targetUser = await dbContext.Users.FindAsync([target.UserId], ct);
        if (targetUser == null) return Results.NotFound();

        targetUser.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        await dbContext.SaveChangesAsync(ct);

        return Results.NoContent();
    }
}

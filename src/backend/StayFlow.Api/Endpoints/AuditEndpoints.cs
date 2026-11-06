using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record AuditEventDto(
    Guid Id,
    Guid? OrganizationId,
    Guid? PropertyId,
    Guid? ActorId,
    string? ActorEmail,
    DateTimeOffset TimestampUtc,
    string EntityName,
    string EntityId,
    string Action,
    string? ChangesJson
);

public static class AuditEndpoints
{
    public static RouteGroupBuilder MapAuditEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", GetAuditEventsAsync)
            .WithName("GetAuditEvents")
            .WithSummary("List recent audit events for the active organization")
            .Produces<List<AuditEventDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        return group;
    }

    private static async Task<IResult> GetAuditEventsAsync(
        [FromQuery] int limit = 50,
        [FromQuery] string? entityName = null,
        [FromQuery] string? action = null,
        [FromQuery] string? actorEmail = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        StayFlowDbContext dbContext = null!,
        ITenantContext tenantContext = null!,
        CancellationToken ct = default)
    {
        if (!tenantContext.OrganizationId.HasValue)
        {
            return Results.Problem("An active organization context is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var orgId = tenantContext.OrganizationId.Value;

        var query = dbContext.AuditEvents
            .Where(a => a.OrganizationId == orgId)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(a => a.EntityName == entityName);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(actorEmail))
            query = query.Where(a => a.ActorEmail != null && a.ActorEmail.Contains(actorEmail));

        if (from.HasValue)
            query = query.Where(a => a.TimestampUtc >= from.Value);

        if (to.HasValue)
            query = query.Where(a => a.TimestampUtc <= to.Value);

        var events = await query
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(a => new AuditEventDto(
                a.Id, a.OrganizationId, a.PropertyId,
                a.ActorId, a.ActorEmail,
                a.TimestampUtc, a.EntityName, a.EntityId,
                a.Action, a.ChangesJson))
            .ToListAsync(ct);

        return Results.Ok(events);
    }
}

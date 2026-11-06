using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Maintenance;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

public record CreateMaintenanceRequest(
    Guid RoomId,
    string Title,
    string? Description,
    int Priority
);

public record UpdateMaintenanceStatusRequest(
    int NewStatus,
    string? AssignedToEmail,
    string? ResolutionNotes
);

public record UpdateMaintenanceDetailsRequest(
    string Title,
    string? Description,
    int Priority
);

public record MaintenanceRequestDto(
    Guid Id,
    Guid PropertyId,
    Guid RoomId,
    string RoomNumber,
    string Title,
    string? Description,
    string Priority,
    int PriorityCode,
    string Status,
    int StatusCode,
    string? ReportedByEmail,
    string? AssignedToEmail,
    string? ResolutionNotes,
    DateTimeOffset? ResolvedAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

public static class MaintenanceEndpoints
{
    public static RouteGroupBuilder MapMaintenanceEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/", ListAsync)
            .WithName("ListMaintenanceRequests")
            .WithSummary("List maintenance requests for a property")
            .Produces<IEnumerable<MaintenanceRequestDto>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .WithName("CreateMaintenanceRequest")
            .WithSummary("Create a new maintenance request")
            .Produces<MaintenanceRequestDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("GetMaintenanceRequestById")
            .WithSummary("Get a maintenance request by ID")
            .Produces<MaintenanceRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/status", UpdateStatusAsync)
            .WithName("UpdateMaintenanceStatus")
            .WithSummary("Update status of a maintenance request")
            .Produces<MaintenanceRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateDetailsAsync)
            .WithName("UpdateMaintenanceDetails")
            .WithSummary("Update title, description, and priority of a maintenance request")
            .Produces<MaintenanceRequestDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return group;
    }

    private static async Task<IResult> ListAsync(
        Guid propertyId,
        [FromQuery] int? status,
        [FromQuery] int? priority,
        [FromQuery] Guid? roomId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var query = dbContext.MaintenanceRequests
            .AsNoTracking()
            .Where(m => m.PropertyId == propertyId && m.OrganizationId == tenantContext.OrganizationId.Value);

        if (status.HasValue)
            query = query.Where(m => (int)m.Status == status.Value);
        if (priority.HasValue)
            query = query.Where(m => (int)m.Priority == priority.Value);
        if (roomId.HasValue)
            query = query.Where(m => m.RoomId == roomId.Value);

        var items = await query
            .OrderByDescending(m => m.Priority)
            .ThenByDescending(m => m.CreatedAtUtc)
            .ToListAsync(ct);

        return Results.Ok(items.Select(ToDto));
    }

    private static async Task<IResult> CreateAsync(
        Guid propertyId,
        [FromBody] CreateMaintenanceRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.Title))
            return Results.Problem("Title is required.", statusCode: StatusCodes.Status400BadRequest);

        if (!Enum.IsDefined(typeof(MaintenancePriority), request.Priority))
            return Results.Problem("Invalid priority.", statusCode: StatusCodes.Status400BadRequest);

        // Verify room belongs to this property & org
        var room = await dbContext.Rooms.FindAsync([request.RoomId], ct);
        if (room == null || room.PropertyId != propertyId || room.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.Problem("Room not found in this property.", statusCode: StatusCodes.Status400BadRequest);

        var mr = MaintenanceRequest.Create(
            organizationId: tenantContext.OrganizationId.Value,
            propertyId: propertyId,
            roomId: request.RoomId,
            roomNumber: room.RoomNumber,
            title: request.Title,
            description: request.Description,
            priority: (MaintenancePriority)request.Priority,
            reportedByEmail: tenantContext.UserEmail);

        dbContext.MaintenanceRequests.Add(mr);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/properties/{propertyId}/maintenance/{mr.Id}", ToDto(mr));
    }

    private static async Task<IResult> GetByIdAsync(
        Guid propertyId,
        Guid id,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var mr = await dbContext.MaintenanceRequests.FindAsync([id], ct);
        if (mr == null || mr.PropertyId != propertyId || mr.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        return Results.Ok(ToDto(mr));
    }

    private static async Task<IResult> UpdateStatusAsync(
        Guid propertyId,
        Guid id,
        [FromBody] UpdateMaintenanceStatusRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var mr = await dbContext.MaintenanceRequests.FindAsync([id], ct);
        if (mr == null || mr.PropertyId != propertyId || mr.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        if (!Enum.IsDefined(typeof(MaintenanceStatus), request.NewStatus))
            return Results.Problem("Invalid status.", statusCode: StatusCodes.Status400BadRequest);

        mr.UpdateStatus((MaintenanceStatus)request.NewStatus, request.AssignedToEmail, request.ResolutionNotes);
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(ToDto(mr));
    }

    private static async Task<IResult> UpdateDetailsAsync(
        Guid propertyId,
        Guid id,
        [FromBody] UpdateMaintenanceDetailsRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var mr = await dbContext.MaintenanceRequests.FindAsync([id], ct);
        if (mr == null || mr.PropertyId != propertyId || mr.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();

        if (string.IsNullOrWhiteSpace(request.Title))
            return Results.Problem("Title is required.", statusCode: StatusCodes.Status400BadRequest);

        if (!Enum.IsDefined(typeof(MaintenancePriority), request.Priority))
            return Results.Problem("Invalid priority.", statusCode: StatusCodes.Status400BadRequest);

        mr.UpdateDetails(request.Title, request.Description, (MaintenancePriority)request.Priority);
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(ToDto(mr));
    }

    private static MaintenanceRequestDto ToDto(MaintenanceRequest m) => new(
        m.Id,
        m.PropertyId,
        m.RoomId,
        m.RoomNumber,
        m.Title,
        m.Description,
        m.Priority.ToString(),
        (int)m.Priority,
        m.Status.ToString(),
        (int)m.Status,
        m.ReportedByEmail,
        m.AssignedToEmail,
        m.ResolutionNotes,
        m.ResolvedAtUtc,
        m.CreatedAtUtc,
        m.UpdatedAtUtc);
}

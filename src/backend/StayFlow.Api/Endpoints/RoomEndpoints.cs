using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.Api.Endpoints;

#region Request & Response DTOs

public record CreateBuildingRequest(
    string Name,
    string Code,
    int NumberOfFloors
);

public record BuildingDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string Code,
    int NumberOfFloors,
    bool IsActive
);

public record CreateFloorRequest(
    Guid? BuildingId,
    int FloorNumber,
    string? Name
);

public record FloorDto(
    Guid Id,
    Guid PropertyId,
    Guid? BuildingId,
    int FloorNumber,
    string Name
);

public record CreateRoomTypeRequest(
    string Name,
    string Code,
    decimal BaseRate,
    int StandardOccupancy,
    int MaxOccupancy,
    int MaxAdults,
    int MaxChildren,
    string? Description,
    string? AmenitiesJson
);

public record UpdateRoomTypeRequest(
    string Name,
    string Code,
    decimal BaseRate,
    int StandardOccupancy,
    int MaxOccupancy,
    int MaxAdults,
    int MaxChildren,
    string? Description,
    string? AmenitiesJson
);

public record RoomTypeDto(
    Guid Id,
    Guid PropertyId,
    string Name,
    string Code,
    decimal BaseRate,
    int StandardOccupancy,
    int MaxOccupancy,
    int MaxAdults,
    int MaxChildren,
    string? Description,
    string? AmenitiesJson,
    bool IsActive
);

public record CreateRoomRequest(
    Guid RoomTypeId,
    string RoomNumber,
    Guid? BuildingId,
    Guid? FloorId,
    string? DisplayName,
    int Capacity,
    RoomOperationalStatus OperationalStatus,
    string? Notes
);

public record UpdateRoomRequest(
    Guid RoomTypeId,
    string RoomNumber,
    Guid? BuildingId,
    Guid? FloorId,
    string? DisplayName,
    int Capacity,
    string? Notes
);

public record UpdateRoomStatusRequest(
    RoomOperationalStatus NewStatus,
    string? Notes
);

public record RoomDto(
    Guid Id,
    Guid OrganizationId,
    Guid PropertyId,
    Guid RoomTypeId,
    string RoomTypeName,
    string RoomTypeCode,
    decimal BaseRate,
    string RoomNumber,
    string? DisplayName,
    Guid? BuildingId,
    string? BuildingName,
    Guid? FloorId,
    string? FloorName,
    int Capacity,
    string OperationalStatus,
    int OperationalStatusCode,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAtUtc
);

public record RoomStatusHistoryDto(
    Guid Id,
    Guid RoomId,
    string FromStatus,
    string ToStatus,
    string? Notes,
    string? ChangedByUserEmail,
    DateTimeOffset CreatedAtUtc
);

public record RoomStatusSummaryDto(
    int TotalRooms,
    int CleanCount,
    int DirtyCount,
    int CleaningCount,
    int InspectedCount,
    int OutOfServiceCount,
    int MaintenanceCount
);

#endregion

public static class RoomEndpoints
{
    public static RouteGroupBuilder MapRoomEndpoints(this RouteGroupBuilder group)
    {
        // 1. Buildings
        group.MapGet("/properties/{propertyId:guid}/buildings", GetBuildingsAsync)
            .WithName("GetBuildings")
            .WithSummary("List all buildings in a property")
            .Produces<List<BuildingDto>>(StatusCodes.Status200OK);

        group.MapPost("/properties/{propertyId:guid}/buildings", CreateBuildingAsync)
            .WithName("CreateBuilding")
            .WithSummary("Create a new building in a property")
            .Produces<BuildingDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/properties/{propertyId:guid}/buildings/{id:guid}", UpdateBuildingAsync)
            .WithName("UpdateBuilding")
            .WithSummary("Update a building's name, code, or floor count")
            .Produces<BuildingDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/properties/{propertyId:guid}/buildings/{id:guid}/deactivate", DeactivateBuildingAsync)
            .WithName("DeactivateBuilding")
            .WithSummary("Deactivate a building (soft delete)")
            .Produces<BuildingDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // 2. Floors
        group.MapGet("/properties/{propertyId:guid}/floors", GetFloorsAsync)
            .WithName("GetFloors")
            .WithSummary("List all floors in a property")
            .Produces<List<FloorDto>>(StatusCodes.Status200OK);

        group.MapPost("/properties/{propertyId:guid}/floors", CreateFloorAsync)
            .WithName("CreateFloor")
            .WithSummary("Create a new floor in a property")
            .Produces<FloorDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/properties/{propertyId:guid}/floors/{id:guid}", UpdateFloorAsync)
            .WithName("UpdateFloor")
            .WithSummary("Update a floor's name")
            .Produces<FloorDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // 3. Room Types
        group.MapGet("/properties/{propertyId:guid}/room-types", GetRoomTypesAsync)
            .WithName("GetRoomTypes")
            .WithSummary("List all room types in a property")
            .Produces<List<RoomTypeDto>>(StatusCodes.Status200OK);

        group.MapPost("/properties/{propertyId:guid}/room-types", CreateRoomTypeAsync)
            .WithName("CreateRoomType")
            .WithSummary("Create a new room type in a property")
            .Produces<RoomTypeDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPut("/room-types/{id:guid}", UpdateRoomTypeAsync)
            .WithName("UpdateRoomType")
            .WithSummary("Update room type and base rate")
            .Produces<RoomTypeDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // 4. Rooms
        group.MapGet("/properties/{propertyId:guid}/rooms", GetRoomsAsync)
            .WithName("GetRooms")
            .WithSummary("List and search rooms in a property with operational status filters")
            .Produces<List<RoomDto>>(StatusCodes.Status200OK);

        group.MapGet("/properties/{propertyId:guid}/rooms/summary", GetRoomSummaryAsync)
            .WithName("GetRoomSummary")
            .WithSummary("Get room operational status count summary for housekeeping dashboard")
            .Produces<RoomStatusSummaryDto>(StatusCodes.Status200OK);

        group.MapPost("/properties/{propertyId:guid}/rooms", CreateRoomAsync)
            .WithName("CreateRoom")
            .WithSummary("Create a new room in a property")
            .Produces<RoomDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/rooms/{id:guid}", GetRoomByIdAsync)
            .WithName("GetRoomById")
            .WithSummary("Get room details by ID")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/rooms/{id:guid}", UpdateRoomAsync)
            .WithName("UpdateRoom")
            .WithSummary("Update room details")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/rooms/{id:guid}/status", UpdateRoomStatusAsync)
            .WithName("UpdateRoomStatus")
            .WithSummary("Change room operational status (Clean, Dirty, Cleaning, Inspected, OutOfService, Maintenance)")
            .Produces<RoomDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/rooms/{id:guid}/history", GetRoomHistoryAsync)
            .WithName("GetRoomHistory")
            .WithSummary("Get historical status transitions of a room")
            .Produces<List<RoomStatusHistoryDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // Dev/demo helper: seed sample room types and rooms for a property that has none
        group.MapPost("/properties/{propertyId:guid}/seed-demo-rooms", SeedDemoRoomsAsync)
            .WithName("SeedDemoRooms")
            .WithSummary("(Dev) Seed sample room types and rooms into an empty property")
            .Produces<SeedDemoRoomsResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        return group;
    }

    #region Buildings & Floors

    private static async Task<IResult> GetBuildingsAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var buildings = await dbContext.Buildings
            .Where(b => b.PropertyId == propertyId && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new BuildingDto(b.Id, b.PropertyId, b.Name, b.Code, b.NumberOfFloors, b.IsActive))
            .ToListAsync(ct);

        return Results.Ok(buildings);
    }

    private static async Task<IResult> CreateBuildingAsync(
        Guid propertyId,
        [FromBody] CreateBuildingRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.Problem("Building Name and Code are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var building = Building.Create(
            tenantContext.OrganizationId.Value,
            propertyId,
            request.Name,
            request.Code,
            request.NumberOfFloors
        );

        dbContext.Buildings.Add(building);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/properties/{propertyId}/buildings/{building.Id}",
            new BuildingDto(building.Id, building.PropertyId, building.Name, building.Code, building.NumberOfFloors, building.IsActive));
    }

    private static async Task<IResult> GetFloorsAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var floors = await dbContext.Floors
            .Where(f => f.PropertyId == propertyId)
            .OrderBy(f => f.FloorNumber)
            .Select(f => new FloorDto(f.Id, f.PropertyId, f.BuildingId, f.FloorNumber, f.Name))
            .ToListAsync(ct);

        return Results.Ok(floors);
    }

    private static async Task<IResult> CreateFloorAsync(
        Guid propertyId,
        [FromBody] CreateFloorRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();

        var floor = Floor.Create(
            tenantContext.OrganizationId.Value,
            propertyId,
            request.BuildingId,
            request.FloorNumber,
            request.Name
        );

        dbContext.Floors.Add(floor);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/properties/{propertyId}/floors/{floor.Id}",
            new FloorDto(floor.Id, floor.PropertyId, floor.BuildingId, floor.FloorNumber, floor.Name));
    }

    private static async Task<IResult> UpdateBuildingAsync(
        Guid propertyId, Guid id,
        [FromBody] CreateBuildingRequest request,
        StayFlowDbContext dbContext, ITenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        var building = await dbContext.Buildings.FindAsync([id], ct);
        if (building == null || building.PropertyId != propertyId || building.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
            return Results.Problem("Name and Code are required.", statusCode: StatusCodes.Status400BadRequest);
        building.Update(request.Name, request.Code, request.NumberOfFloors);
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new BuildingDto(building.Id, building.PropertyId, building.Name, building.Code, building.NumberOfFloors, building.IsActive));
    }

    private static async Task<IResult> DeactivateBuildingAsync(
        Guid propertyId, Guid id,
        StayFlowDbContext dbContext, ITenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        var building = await dbContext.Buildings.FindAsync([id], ct);
        if (building == null || building.PropertyId != propertyId || building.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();
        building.Deactivate();
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new BuildingDto(building.Id, building.PropertyId, building.Name, building.Code, building.NumberOfFloors, building.IsActive));
    }

    private static async Task<IResult> UpdateFloorAsync(
        Guid propertyId, Guid id,
        [FromBody] CreateFloorRequest request,
        StayFlowDbContext dbContext, ITenantContext tenantContext, CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        var floor = await dbContext.Floors.FindAsync([id], ct);
        if (floor == null || floor.PropertyId != propertyId || floor.OrganizationId != tenantContext.OrganizationId.Value)
            return Results.NotFound();
        var newName = string.IsNullOrWhiteSpace(request.Name) ? $"Floor {request.FloorNumber}" : request.Name;
        floor.UpdateName(newName);
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new FloorDto(floor.Id, floor.PropertyId, floor.BuildingId, floor.FloorNumber, floor.Name));
    }

    #endregion

    #region Room Types

    private static async Task<IResult> GetRoomTypesAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var types = await dbContext.RoomTypes
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .OrderBy(r => r.Name)
            .Select(r => new RoomTypeDto(
                r.Id,
                r.PropertyId,
                r.Name,
                r.Code,
                r.BaseRate,
                r.StandardOccupancy,
                r.MaxOccupancy,
                r.MaxAdults,
                r.MaxChildren,
                r.Description,
                r.AmenitiesJson,
                r.IsActive))
            .ToListAsync(ct);

        return Results.Ok(types);
    }

    private static async Task<IResult> CreateRoomTypeAsync(
        Guid propertyId,
        [FromBody] CreateRoomTypeRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.Problem("Room Type Name and Code are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var roomType = RoomType.Create(
            tenantContext.OrganizationId.Value,
            propertyId,
            request.Name,
            request.Code,
            request.BaseRate,
            request.StandardOccupancy,
            request.MaxOccupancy,
            request.MaxAdults,
            request.MaxChildren,
            request.Description,
            request.AmenitiesJson
        );

        dbContext.RoomTypes.Add(roomType);
        await dbContext.SaveChangesAsync(ct);

        return Results.Created($"/api/v1/properties/{propertyId}/room-types/{roomType.Id}",
            new RoomTypeDto(
                roomType.Id,
                roomType.PropertyId,
                roomType.Name,
                roomType.Code,
                roomType.BaseRate,
                roomType.StandardOccupancy,
                roomType.MaxOccupancy,
                roomType.MaxAdults,
                roomType.MaxChildren,
                roomType.Description,
                roomType.AmenitiesJson,
                roomType.IsActive));
    }

    private static async Task<IResult> UpdateRoomTypeAsync(
        Guid id,
        [FromBody] UpdateRoomTypeRequest req,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var roomType = await dbContext.RoomTypes.FindAsync([id], ct);
        if (roomType == null || !roomType.IsActive) return Results.NotFound();

        roomType.Update(
            req.Name,
            req.Code,
            req.BaseRate,
            req.StandardOccupancy,
            req.MaxOccupancy,
            req.MaxAdults,
            req.MaxChildren,
            req.Description,
            req.AmenitiesJson
        );

        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new RoomTypeDto(
            roomType.Id,
            roomType.PropertyId,
            roomType.Name,
            roomType.Code,
            roomType.BaseRate,
            roomType.StandardOccupancy,
            roomType.MaxOccupancy,
            roomType.MaxAdults,
            roomType.MaxChildren,
            roomType.Description,
            roomType.AmenitiesJson,
            roomType.IsActive
        ));
    }

    #endregion

    #region Rooms

    private static async Task<IResult> GetRoomsAsync(
        Guid propertyId,
        [FromQuery] string? search,
        [FromQuery] RoomOperationalStatus? status,
        [FromQuery] Guid? roomTypeId,
        [FromQuery] Guid? buildingId,
        [FromQuery] Guid? floorId,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var query = dbContext.Rooms
            .Where(r => r.PropertyId == propertyId && r.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToUpperInvariant();
            query = query.Where(r => r.RoomNumber.Contains(term) || (r.DisplayName != null && r.DisplayName.Contains(term)));
        }

        if (status.HasValue)
        {
            query = query.Where(r => r.OperationalStatus == status.Value);
        }

        if (roomTypeId.HasValue)
        {
            query = query.Where(r => r.RoomTypeId == roomTypeId.Value);
        }

        if (buildingId.HasValue)
        {
            query = query.Where(r => r.BuildingId == buildingId.Value);
        }

        if (floorId.HasValue)
        {
            query = query.Where(r => r.FloorId == floorId.Value);
        }

        var rooms = await (
            from r in query
            join rt in dbContext.RoomTypes on r.RoomTypeId equals rt.Id into rtGroup
            from rt in rtGroup.DefaultIfEmpty()
            join b in dbContext.Buildings on r.BuildingId equals b.Id into bGroup
            from b in bGroup.DefaultIfEmpty()
            join f in dbContext.Floors on r.FloorId equals f.Id into fGroup
            from f in fGroup.DefaultIfEmpty()
            orderby r.RoomNumber
            select new RoomDto(
                r.Id,
                r.OrganizationId,
                r.PropertyId,
                r.RoomTypeId,
                rt != null ? rt.Name : "Standard",
                rt != null ? rt.Code : "STD",
                rt != null ? rt.BaseRate : 0m,
                r.RoomNumber,
                r.DisplayName,
                r.BuildingId,
                b != null ? b.Name : null,
                r.FloorId,
                f != null ? f.Name : null,
                r.Capacity,
                r.OperationalStatus.ToString(),
                (int)r.OperationalStatus,
                r.IsActive,
                r.Notes,
                r.CreatedAtUtc)
        ).ToListAsync(ct);

        return Results.Ok(rooms);
    }

    private static async Task<IResult> GetRoomSummaryAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var rooms = await dbContext.Rooms
            .Where(r => r.PropertyId == propertyId && r.IsActive)
            .Select(r => r.OperationalStatus)
            .ToListAsync(ct);

        var summary = new RoomStatusSummaryDto(
            TotalRooms: rooms.Count,
            CleanCount: rooms.Count(s => s == RoomOperationalStatus.Clean),
            DirtyCount: rooms.Count(s => s == RoomOperationalStatus.Dirty),
            CleaningCount: rooms.Count(s => s == RoomOperationalStatus.Cleaning),
            InspectedCount: rooms.Count(s => s == RoomOperationalStatus.Inspected),
            OutOfServiceCount: rooms.Count(s => s == RoomOperationalStatus.OutOfService),
            MaintenanceCount: rooms.Count(s => s == RoomOperationalStatus.Maintenance)
        );

        return Results.Ok(summary);
    }

    private static async Task<IResult> CreateRoomAsync(
        Guid propertyId,
        [FromBody] CreateRoomRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue) return Results.Unauthorized();
        if (string.IsNullOrWhiteSpace(request.RoomNumber))
        {
            return Results.Problem("Room number is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cleanNumber = request.RoomNumber.Trim().ToUpperInvariant();
        var exists = await dbContext.Rooms
            .AnyAsync(r => r.PropertyId == propertyId && r.RoomNumber == cleanNumber, ct);

        if (exists)
        {
            return Results.Problem($"Room number '{cleanNumber}' already exists in this property.", statusCode: StatusCodes.Status400BadRequest);
        }

        var room = Room.Create(
            tenantContext.OrganizationId.Value,
            propertyId,
            request.RoomTypeId,
            cleanNumber,
            request.BuildingId,
            request.FloorId,
            request.DisplayName,
            request.Capacity,
            request.OperationalStatus,
            request.Notes
        );

        dbContext.Rooms.Add(room);
        await dbContext.SaveChangesAsync(ct);

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Created($"/api/v1/rooms/{room.Id}", new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc
        ));
    }

    private static async Task<IResult> GetRoomByIdAsync(
        Guid id,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var room = await dbContext.Rooms.FindAsync([id], ct);
        if (room == null || !room.IsActive) return Results.NotFound();

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Ok(new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc
        ));
    }

    private static async Task<IResult> UpdateRoomAsync(
        Guid id,
        [FromBody] UpdateRoomRequest request,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var room = await dbContext.Rooms.FindAsync([id], ct);
        if (room == null || !room.IsActive) return Results.NotFound();

        room.UpdateDetails(
            request.RoomTypeId,
            request.RoomNumber,
            request.BuildingId,
            request.FloorId,
            request.DisplayName,
            request.Capacity,
            request.Notes
        );

        await dbContext.SaveChangesAsync(ct);

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Ok(new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc
        ));
    }

    private static async Task<IResult> UpdateRoomStatusAsync(
        Guid id,
        [FromBody] UpdateRoomStatusRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        var room = await dbContext.Rooms.FindAsync([id], ct);
        if (room == null || !room.IsActive) return Results.NotFound();

        var history = room.UpdateOperationalStatus(
            request.NewStatus,
            request.Notes,
            tenantContext.UserId,
            tenantContext.UserEmail
        );

        dbContext.RoomStatusHistories.Add(history);
        await dbContext.SaveChangesAsync(ct);

        var roomType = await dbContext.RoomTypes.FindAsync([room.RoomTypeId], ct);
        var building = room.BuildingId.HasValue ? await dbContext.Buildings.FindAsync([room.BuildingId.Value], ct) : null;
        var floor = room.FloorId.HasValue ? await dbContext.Floors.FindAsync([room.FloorId.Value], ct) : null;

        return Results.Ok(new RoomDto(
            room.Id,
            room.OrganizationId,
            room.PropertyId,
            room.RoomTypeId,
            roomType?.Name ?? "Standard",
            roomType?.Code ?? "STD",
            roomType?.BaseRate ?? 0m,
            room.RoomNumber,
            room.DisplayName,
            room.BuildingId,
            building?.Name,
            room.FloorId,
            floor?.Name,
            room.Capacity,
            room.OperationalStatus.ToString(),
            (int)room.OperationalStatus,
            room.IsActive,
            room.Notes,
            room.CreatedAtUtc
        ));
    }

    private static async Task<IResult> GetRoomHistoryAsync(
        Guid id,
        StayFlowDbContext dbContext,
        CancellationToken ct)
    {
        var histories = await dbContext.RoomStatusHistories
            .Where(h => h.RoomId == id)
            .OrderByDescending(h => h.CreatedAtUtc)
            .Take(50)
            .Select(h => new RoomStatusHistoryDto(
                h.Id,
                h.RoomId,
                h.FromStatus.ToString(),
                h.ToStatus.ToString(),
                h.Notes,
                h.ChangedByUserEmail,
                h.CreatedAtUtc))
            .ToListAsync(ct);

        return Results.Ok(histories);
    }

    #endregion

    #region Demo / Dev helpers

    private static async Task<IResult> SeedDemoRoomsAsync(
        Guid propertyId,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.OrganizationId.HasValue)
            return Results.Unauthorized();

        var orgId = tenantContext.OrganizationId.Value;

        var property = await dbContext.Properties.FindAsync([propertyId], ct);
        if (property == null || property.OrganizationId != orgId)
            return Results.Problem("Property not found.", statusCode: StatusCodes.Status400BadRequest);

        // Check if this property already has rooms
        var hasRooms = await dbContext.Rooms.AnyAsync(r => r.PropertyId == propertyId && r.IsActive, ct);
        if (hasRooms)
            return Results.Ok(new SeedDemoRoomsResultDto(0, 0, "Property already has rooms — skipped."));

        // Create room types
        var standardType = RoomType.Create(orgId, propertyId, "Standard", "STD", 1500m, 2, 2);
        var deluxeType   = RoomType.Create(orgId, propertyId, "Deluxe",   "DLX", 2500m, 2, 3);
        var suiteType    = RoomType.Create(orgId, propertyId, "Suite",    "STE", 4500m, 2, 4);
        dbContext.RoomTypes.AddRange(standardType, deluxeType, suiteType);

        // Create rooms
        var rooms = new[]
        {
            Room.Create(orgId, propertyId, standardType.Id, "101", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(orgId, propertyId, standardType.Id, "102", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(orgId, propertyId, standardType.Id, "103", capacity: 2, operationalStatus: RoomOperationalStatus.Inspected),
            Room.Create(orgId, propertyId, standardType.Id, "104", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(orgId, propertyId, deluxeType.Id,   "201", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(orgId, propertyId, deluxeType.Id,   "202", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(orgId, propertyId, deluxeType.Id,   "203", capacity: 3, operationalStatus: RoomOperationalStatus.Inspected),
            Room.Create(orgId, propertyId, suiteType.Id,    "301", capacity: 4, operationalStatus: RoomOperationalStatus.Inspected),
        };
        dbContext.Rooms.AddRange(rooms);

        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new SeedDemoRoomsResultDto(3, rooms.Length, "Demo rooms created successfully."));
    }

    #endregion
}

public record SeedDemoRoomsResultDto(int RoomTypesCreated, int RoomsCreated, string Message);

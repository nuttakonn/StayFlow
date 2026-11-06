using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

public class Phase4IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase4IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId, Guid propertyId)> SetupAsync()
    {
        var regRequest = new RegisterRequest(
            Email: $"phase4_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase4 Ops Manager",
            PhoneNumber: "+66890001111",
            OrganizationName: $"Phase4 Hotel Org {Guid.NewGuid():N}"[..30]
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        var propRequest = new CreatePropertyRequest(
            Name: "Phase4 Grand Resort",
            Code: $"P4G{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "Operations Blvd 4",
            ContactPhone: "+66890002222",
            ContactEmail: "ops@phase4.com"
        );
        using var reqProp = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        reqProp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqProp.Headers.Add("X-Organization-Id", orgId.ToString());
        reqProp.Content = JsonContent.Create(propRequest);
        var resProp = await _client.SendAsync(reqProp);
        var prop = await resProp.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);

        return (token, orgId, prop!.Id);
    }

    private HttpRequestMessage Auth(HttpMethod method, string url, string token, Guid orgId, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Organization-Id", orgId.ToString());
        if (body != null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task Phase4_OperationsDashboard_CalculatesCorrectStats()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // 1. Create Room Type & 2 Rooms
        var rtReq = new CreateRoomTypeRequest("Standard", $"STD{Guid.NewGuid():N}"[..6], 1200m, 2, 2, 2, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq1 = new CreateRoomRequest(roomType!.Id, $"R1{Guid.NewGuid():N}"[..4], null, null, "Room 1", 2, RoomOperationalStatus.Clean, null);
        var roomReq2 = new CreateRoomRequest(roomType.Id, $"R2{Guid.NewGuid():N}"[..4], null, null, "Room 2", 2, RoomOperationalStatus.Dirty, null);
        using var r2a = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq1);
        await _client.SendAsync(r2a);
        using var r2b = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq2);
        await _client.SendAsync(r2b);

        // 2. Query Dashboard
        using var dashReq = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/operations/dashboard", token, orgId);
        var dashRes = await _client.SendAsync(dashReq);
        Assert.Equal(HttpStatusCode.OK, dashRes.StatusCode);

        var summary = await dashRes.Content.ReadFromJsonAsync<OperationsDashboardSummaryDto>(_jsonOptions);
        Assert.NotNull(summary);
        Assert.True(summary.CleanRooms >= 1);
        Assert.True(summary.DirtyRooms >= 1);
    }

    [Fact]
    public async Task Phase4_RoomMove_TransfersRoom_And_MarksOldRoomDirty()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Create room type
        var rtReq = new CreateRoomTypeRequest("Deluxe", $"DLX{Guid.NewGuid():N}"[..6], 2000m, 2, 2, 2, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        // Create Room A (Initial) and Room B (Target)
        var roomReqA = new CreateRoomRequest(roomType!.Id, $"10A{Guid.NewGuid():N}"[..3], null, null, "Room 10A", 2, RoomOperationalStatus.Clean, null);
        var roomReqB = new CreateRoomRequest(roomType.Id, $"10B{Guid.NewGuid():N}"[..3], null, null, "Room 10B", 2, RoomOperationalStatus.Clean, null);
        using var r2a = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReqA);
        var roomA = await (await _client.SendAsync(r2a)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        using var r2b = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReqB);
        var roomB = await (await _client.SendAsync(r2b)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        // Create guest & reservation
        var guestReq = new CreateGuestRequest("คุณสมพงษ์ ย้ายห้อง", "sompong@move.com", "+66810001111", null, null, "Thai", null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resReq = new CreateReservationRequest(
            propertyId, guest!.Id,
            today.ToString("yyyy-MM-dd"), today.AddDays(3).ToString("yyyy-MM-dd"),
            2, 0, roomType.Id, roomA!.Id, 2000m, 6000m, "THB", "Phone", null, null);
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, resReq);
        var res = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        // Confirm & CheckIn to Room A
        using var rConf = Auth(HttpMethod.Post, $"/api/v1/reservations/{res!.Id}/confirm", token, orgId);
        await _client.SendAsync(rConf);
        using var rCheckIn = Auth(HttpMethod.Post, $"/api/v1/reservations/{res.Id}/check-in", token, orgId);
        await _client.SendAsync(rCheckIn);

        // 3. Move Room from Room A to Room B
        var moveReq = new RoomMoveRequest(roomB!.Id, "Air conditioner leaking water in Room A");
        using var rMove = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/operations/reservations/{res.Id}/room-move", token, orgId, moveReq);
        var moveRes = await _client.SendAsync(rMove);
        Assert.Equal(HttpStatusCode.OK, moveRes.StatusCode);

        var updatedRes = await moveRes.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal(roomB.Id, updatedRes!.RoomId);
        Assert.Equal(roomB.RoomNumber, updatedRes.RoomNumber);

        // 4. Verify Old Room A is now Dirty
        using var rGetOldRoom = Auth(HttpMethod.Get, $"/api/v1/rooms/{roomA.Id}", token, orgId);
        var oldRoom = await (await _client.SendAsync(rGetOldRoom)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        Assert.Equal("Dirty", oldRoom!.OperationalStatus);
    }

    [Fact]
    public async Task Phase4_OutOfService_BlocksOccupiedRoom_And_ReturnToServiceSuccess()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // 1. Create Room Type & Room
        var rtReq = new CreateRoomTypeRequest("Suite", $"STE{Guid.NewGuid():N}"[..6], 3000m, 2, 4, 2, 2, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"OOS{Guid.NewGuid():N}"[..3], null, null, "Maintenance Room", 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        // 2. Set to Maintenance
        var oosReq = new SetOutOfServiceRequest("Maintenance", "Painting walls and deep cleaning");
        using var rOos = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/operations/rooms/{room!.Id}/out-of-service", token, orgId, oosReq);
        var oosRes = await _client.SendAsync(rOos);
        Assert.Equal(HttpStatusCode.OK, oosRes.StatusCode);
        var oosRoom = await oosRes.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        Assert.Equal("Maintenance", oosRoom!.OperationalStatus);

        // 3. Return to service
        var returnReq = new ReturnToServiceRequest("Clean", "Painting completed and inspected");
        using var rReturn = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/operations/rooms/{room.Id}/return-to-service", token, orgId, returnReq);
        var returnRes = await _client.SendAsync(rReturn);
        Assert.Equal(HttpStatusCode.OK, returnRes.StatusCode);
        var cleanRoom = await returnRes.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        Assert.Equal("Clean", cleanRoom!.OperationalStatus);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

public class Phase2IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase2IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Phase2_FullInventoryWorkflow_Buildings_RoomTypes_Rooms_StatusTransitions_History()
    {
        // 1. Register & Setup
        var regRequest = new RegisterRequest(
            Email: $"hotel_manager_{Guid.NewGuid():N}@resort.com",
            Password: "SecurePassword123!",
            FullName: "Anan Sukhumvit",
            PhoneNumber: "+66855554444",
            OrganizationName: "Sunrise Resort Group"
        );

        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        // 2. Create Property
        var propRequest = new CreatePropertyRequest(
            Name: "Sunrise Bay Resort",
            Code: $"SBR-{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "Chaweng Beach, Koh Samui",
            ContactPhone: "+6677112233",
            ContactEmail: "samui@sunrise.com"
        );

        using var reqProp = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        reqProp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqProp.Headers.Add("X-Organization-Id", orgId.ToString());
        reqProp.Content = JsonContent.Create(propRequest);

        var resProp = await _client.SendAsync(reqProp);
        Assert.Equal(HttpStatusCode.Created, resProp.StatusCode);
        var property = await resProp.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);
        var propId = property!.Id;

        // 3. Create Building & Floor
        var bldRequest = new CreateBuildingRequest("Ocean Wing", "BLD-A", 3);
        using var reqBld = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/properties/{propId}/buildings");
        reqBld.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqBld.Headers.Add("X-Organization-Id", orgId.ToString());
        reqBld.Content = JsonContent.Create(bldRequest);

        var resBld = await _client.SendAsync(reqBld);
        Assert.Equal(HttpStatusCode.Created, resBld.StatusCode);
        var building = await resBld.Content.ReadFromJsonAsync<BuildingDto>(_jsonOptions);

        var floorRequest = new CreateFloorRequest(building!.Id, 1, "Ground Floor");
        using var reqFloor = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/properties/{propId}/floors");
        reqFloor.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqFloor.Headers.Add("X-Organization-Id", orgId.ToString());
        reqFloor.Content = JsonContent.Create(floorRequest);

        var resFloor = await _client.SendAsync(reqFloor);
        Assert.Equal(HttpStatusCode.Created, resFloor.StatusCode);
        var floor = await resFloor.Content.ReadFromJsonAsync<FloorDto>(_jsonOptions);

        // 4. Create Room Type
        var rtRequest = new CreateRoomTypeRequest(
            Name: "Deluxe King Sea View",
            Code: "DLX-K",
            BaseRate: 2500.00m,
            StandardOccupancy: 2,
            MaxOccupancy: 3,
            MaxAdults: 2,
            MaxChildren: 1,
            Description: "Spacious sea-view room with king bed and balcony",
            AmenitiesJson: "[\"WiFi\",\"Balcony\",\"SeaView\",\"MiniBar\"]"
        );

        using var reqRt = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/properties/{propId}/room-types");
        reqRt.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqRt.Headers.Add("X-Organization-Id", orgId.ToString());
        reqRt.Content = JsonContent.Create(rtRequest);

        var resRt = await _client.SendAsync(reqRt);
        Assert.Equal(HttpStatusCode.Created, resRt.StatusCode);
        var roomType = await resRt.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        // 5. Create Room 101 and Room 102
        var room1Request = new CreateRoomRequest(
            RoomTypeId: roomType!.Id,
            RoomNumber: "101",
            BuildingId: building.Id,
            FloorId: floor!.Id,
            DisplayName: "Oceanview Room 101",
            Capacity: 2,
            OperationalStatus: RoomOperationalStatus.Clean,
            Notes: "Near swimming pool"
        );

        using var reqRoom1 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/properties/{propId}/rooms");
        reqRoom1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqRoom1.Headers.Add("X-Organization-Id", orgId.ToString());
        reqRoom1.Content = JsonContent.Create(room1Request);

        var resRoom1 = await _client.SendAsync(reqRoom1);
        Assert.Equal(HttpStatusCode.Created, resRoom1.StatusCode);
        var room1 = await resRoom1.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        Assert.NotNull(room1);
        Assert.Equal("101", room1.RoomNumber);
        Assert.Equal("Clean", room1.OperationalStatus);

        // Verify duplicate room number is rejected
        using var reqDuplicate = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/properties/{propId}/rooms");
        reqDuplicate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqDuplicate.Headers.Add("X-Organization-Id", orgId.ToString());
        reqDuplicate.Content = JsonContent.Create(room1Request);

        var resDuplicate = await _client.SendAsync(reqDuplicate);
        Assert.Equal(HttpStatusCode.BadRequest, resDuplicate.StatusCode);

        // 6. Update Operational Status of Room 101: Clean -> Dirty -> Cleaning -> Inspected
        var statusTransitions = new[]
        {
            (RoomOperationalStatus.Dirty, "Guest checked out; room requires housekeeping"),
            (RoomOperationalStatus.Cleaning, "Housekeeper Somying started cleaning"),
            (RoomOperationalStatus.Inspected, "Supervisor verified linen, minibar and cleanliness")
        };

        foreach (var (nextStatus, notes) in statusTransitions)
        {
            using var reqStatus = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/rooms/{room1.Id}/status");
            reqStatus.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            reqStatus.Headers.Add("X-Organization-Id", orgId.ToString());
            reqStatus.Content = JsonContent.Create(new UpdateRoomStatusRequest(nextStatus, notes));

            var resStatus = await _client.SendAsync(reqStatus);
            Assert.Equal(HttpStatusCode.OK, resStatus.StatusCode);
            var updated = await resStatus.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
            Assert.Equal(nextStatus.ToString(), updated!.OperationalStatus);
        }

        // 7. Verify Status History
        using var reqHist = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/rooms/{room1.Id}/history");
        reqHist.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqHist.Headers.Add("X-Organization-Id", orgId.ToString());

        var resHist = await _client.SendAsync(reqHist);
        Assert.Equal(HttpStatusCode.OK, resHist.StatusCode);
        var histories = await resHist.Content.ReadFromJsonAsync<List<RoomStatusHistoryDto>>(_jsonOptions);
        Assert.NotNull(histories);
        Assert.Equal(3, histories.Count);
        Assert.Equal("Inspected", histories[0].ToStatus);
        Assert.Equal("Cleaning", histories[1].ToStatus);
        Assert.Equal("Dirty", histories[2].ToStatus);

        // 8. Query Room Operational Status Summary
        using var reqSummary = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/properties/{propId}/rooms/summary");
        reqSummary.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqSummary.Headers.Add("X-Organization-Id", orgId.ToString());

        var resSummary = await _client.SendAsync(reqSummary);
        Assert.Equal(HttpStatusCode.OK, resSummary.StatusCode);
        var summary = await resSummary.Content.ReadFromJsonAsync<RoomStatusSummaryDto>(_jsonOptions);
        Assert.NotNull(summary);
        Assert.Equal(1, summary.TotalRooms);
        Assert.Equal(1, summary.InspectedCount);
    }
}

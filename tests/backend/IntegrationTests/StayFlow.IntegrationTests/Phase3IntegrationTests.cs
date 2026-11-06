using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

/// <summary>
/// Phase 3 integration tests: Guests, Reservations, Availability, Lifecycle, and Double-Booking Prevention.
///
/// NOTE: These tests use InMemory database (no PostgreSQL required).
/// The application-level overlap check is validated, but the PostgreSQL-level unique index
/// is not testable with InMemory. See ADR-0007 for details.
/// </summary>
public class Phase3IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase3IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId, Guid propertyId)> SetupAsync()
    {
        // Register user + org
        var regRequest = new RegisterRequest(
            Email: $"phase3_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase3 Manager",
            PhoneNumber: "+66999990000",
            OrganizationName: $"Phase3 Hotel Group {Guid.NewGuid():N}"[..30]
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        Assert.Equal(HttpStatusCode.OK, regResponse.StatusCode);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        // Create property
        var propRequest = new CreatePropertyRequest(
            Name: "Test Hotel Phase3",
            Code: $"P3H{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "123 Test Street",
            ContactPhone: "+66800000001",
            ContactEmail: "phase3@test.com"
        );
        using var reqProp = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        reqProp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqProp.Headers.Add("X-Organization-Id", orgId.ToString());
        reqProp.Content = JsonContent.Create(propRequest);
        var resProp = await _client.SendAsync(reqProp);
        Assert.Equal(HttpStatusCode.Created, resProp.StatusCode);
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
    public async Task Phase3_GuestCRUD_CreateSearchUpdate()
    {
        var (token, orgId, _) = await SetupAsync();

        // 1. Create guest
        var createReq = new CreateGuestRequest(
            FullName: "สมชาย ใจดี",
            Email: $"somchai_{Guid.NewGuid():N}@test.com",
            Phone: "+66812345678",
            NationalId: "1234567890123",
            PassportNumber: null,
            Nationality: "Thai",
            Notes: "VIP Guest"
        );
        using var r1 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, createReq);
        var res1 = await _client.SendAsync(r1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var guest = await res1.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);
        Assert.NotNull(guest);
        Assert.Equal("สมชาย ใจดี", guest.FullName);
        Assert.Equal("Thai", guest.Nationality);

        // 2. Search guest by name
        using var r2 = Auth(HttpMethod.Get, "/api/v1/guests?search=สมชาย", token, orgId);
        var res2 = await _client.SendAsync(r2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);
        var searchResult = await res2.Content.ReadFromJsonAsync<JsonElement>(_jsonOptions);
        Assert.True(searchResult.GetProperty("total").GetInt32() >= 1);

        // 3. Get guest by ID
        using var r3 = Auth(HttpMethod.Get, $"/api/v1/guests/{guest.Id}", token, orgId);
        var res3 = await _client.SendAsync(r3);
        Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
        var fetchedGuest = await res3.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);
        Assert.Equal(guest.Id, fetchedGuest!.Id);

        // 4. Update guest
        var updateReq = new UpdateGuestRequest(
            FullName: "สมชาย ใจดี (อัพเดท)",
            Email: createReq.Email,
            Phone: "+66899999999",
            NationalId: "1234567890123",
            PassportNumber: "AB123456",
            Nationality: "Thai",
            Notes: "Super VIP"
        );
        using var r4 = Auth(HttpMethod.Put, $"/api/v1/guests/{guest.Id}", token, orgId, updateReq);
        var res4 = await _client.SendAsync(r4);
        Assert.Equal(HttpStatusCode.OK, res4.StatusCode);
        var updatedGuest = await res4.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);
        Assert.Equal("สมชาย ใจดี (อัพเดท)", updatedGuest!.FullName);
        Assert.Equal("AB123456", updatedGuest.PassportNumber);
    }

    [Fact]
    public async Task Phase3_FullReservationLifecycle_Create_Confirm_AssignRoom_CheckIn_CheckOut()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // 1. Create room type
        var rtReq = new CreateRoomTypeRequest(
            Name: "Deluxe King",
            Code: $"DLX{Guid.NewGuid():N}"[..6],
            BaseRate: 2000m,
            StandardOccupancy: 2,
            MaxOccupancy: 3,
            MaxAdults: 2,
            MaxChildren: 1,
            Description: "Luxury king bed room",
            AmenitiesJson: null
        );
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var res1 = await _client.SendAsync(r1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var roomType = await res1.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        // 2. Create room
        var roomReq = new CreateRoomRequest(
            RoomTypeId: roomType!.Id,
            RoomNumber: $"R{Guid.NewGuid():N}"[..4],
            BuildingId: null,
            FloorId: null,
            DisplayName: "Deluxe Test Room",
            Capacity: 2,
            OperationalStatus: RoomOperationalStatus.Clean,
            Notes: "Test room"
        );
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var res2 = await _client.SendAsync(r2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var room = await res2.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        // 3. Create guest
        var guestReq = new CreateGuestRequest(
            FullName: "นายทดสอบ วงศ์ดี",
            Email: $"test_{Guid.NewGuid():N}@lifecycle.com",
            Phone: "+66811111111",
            NationalId: null,
            PassportNumber: "TH99999",
            Nationality: "Thai",
            Notes: null
        );
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var res3 = await _client.SendAsync(r3);
        Assert.Equal(HttpStatusCode.Created, res3.StatusCode);
        var guest = await res3.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        // 4. Create reservation (Pending, no room yet)
        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        var checkOut = checkIn.AddDays(3);
        var createResReq = new CreateReservationRequest(
            PropertyId: propertyId,
            GuestId: guest!.Id,
            CheckInDate: checkIn.ToString("yyyy-MM-dd"),
            CheckOutDate: checkOut.ToString("yyyy-MM-dd"),
            Adults: 2,
            Children: 0,
            RoomTypeId: roomType.Id,
            RoomId: null,
            QuotedRate: 2000m,
            TotalAmount: 6000m,
            Currency: "THB",
            Source: "Phone",
            SpecialRequests: "High floor if possible",
            Notes: "Corporate booking"
        );
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createResReq);
        var res4 = await _client.SendAsync(r4);
        Assert.Equal(HttpStatusCode.Created, res4.StatusCode);
        var reservation = await res4.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.NotNull(reservation);
        Assert.Equal("Pending", reservation.Status);
        Assert.Equal(3, reservation.NumberOfNights);
        Assert.Null(reservation.RoomNumber);

        // 5. Confirm reservation
        using var r5 = Auth(HttpMethod.Post, $"/api/v1/reservations/{reservation.Id}/confirm", token, orgId);
        var res5 = await _client.SendAsync(r5);
        Assert.Equal(HttpStatusCode.OK, res5.StatusCode);
        var confirmed = await res5.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal("Confirmed", confirmed!.Status);
        Assert.Equal(2, confirmed.Timeline.Count); // Pending -> Confirmed

        // 6. Assign room
        using var r6 = Auth(HttpMethod.Post, $"/api/v1/reservations/{reservation.Id}/assign-room", token, orgId,
            new AssignRoomRequest(room!.Id));
        var res6 = await _client.SendAsync(r6);
        Assert.Equal(HttpStatusCode.OK, res6.StatusCode);
        var withRoom = await res6.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal(room.RoomNumber, withRoom!.RoomNumber);

        // 7. Check in
        using var r7 = Auth(HttpMethod.Post, $"/api/v1/reservations/{reservation.Id}/check-in", token, orgId);
        var res7 = await _client.SendAsync(r7);
        Assert.Equal(HttpStatusCode.OK, res7.StatusCode);
        var checkedIn = await res7.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal("CheckedIn", checkedIn!.Status);
        Assert.NotNull(checkedIn.CheckedInAtUtc);

        // 8. Check out
        using var r8 = Auth(HttpMethod.Post, $"/api/v1/reservations/{reservation.Id}/check-out", token, orgId);
        var res8 = await _client.SendAsync(r8);
        Assert.Equal(HttpStatusCode.OK, res8.StatusCode);
        var checkedOut = await res8.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal("CheckedOut", checkedOut!.Status);
        Assert.NotNull(checkedOut.CheckedOutAtUtc);

        // 9. Verify full timeline
        using var r9 = Auth(HttpMethod.Get, $"/api/v1/reservations/{reservation.Id}", token, orgId);
        var res9 = await _client.SendAsync(r9);
        Assert.Equal(HttpStatusCode.OK, res9.StatusCode);
        var final = await res9.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.True(final!.Timeline.Count >= 4); // Pending, Confirmed, RoomAssigned, CheckedIn, CheckedOut
    }

    [Fact]
    public async Task Phase3_DoubleBooking_SameRoom_SameDates_SecondRequestFails()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Create room type + room
        var rtReq = new CreateRoomTypeRequest("Standard", $"STD{Guid.NewGuid():N}"[..6], 1500m, 2, 2, 1, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var res1 = await _client.SendAsync(r1);
        var roomType = await res1.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"DB{Guid.NewGuid():N}"[..4], null, null, "Double Booking Test", 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var res2 = await _client.SendAsync(r2);
        var room = await res2.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        // Create guest
        var guestReq = new CreateGuestRequest($"Guest{Guid.NewGuid():N}"[..10], $"db_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var res3 = await _client.SendAsync(r3);
        var guest = await res3.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5));
        var checkOut = checkIn.AddDays(3);

        // First reservation with room assigned
        var createRes1 = new CreateReservationRequest(
            propertyId, guest!.Id,
            checkIn.ToString("yyyy-MM-dd"), checkOut.ToString("yyyy-MM-dd"),
            2, 0, roomType.Id, room!.Id, 1500m, 4500m, "THB", "Phone", null, "First booking");

        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createRes1);
        var res4 = await _client.SendAsync(r4);
        Assert.Equal(HttpStatusCode.Created, res4.StatusCode);

        // Confirm first
        var firstRes = await res4.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        using var r4c = Auth(HttpMethod.Post, $"/api/v1/reservations/{firstRes!.Id}/confirm", token, orgId);
        await _client.SendAsync(r4c);

        // Second reservation for SAME room, SAME dates → must fail with 409
        var createRes2 = new CreateReservationRequest(
            propertyId, guest.Id,
            checkIn.ToString("yyyy-MM-dd"), checkOut.ToString("yyyy-MM-dd"),
            1, 0, roomType.Id, room.Id, 1500m, 4500m, "THB", "WalkIn", null, "Second booking (should fail)");

        using var r5 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createRes2);
        var res5 = await _client.SendAsync(r5);
        // Must be 409 Conflict
        Assert.Equal(HttpStatusCode.Conflict, res5.StatusCode);
    }

    [Fact]
    public async Task Phase3_Cancellation_ReleasesInventory_AllowsNewBooking()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Setup: room type + room + guest
        var rtReq = new CreateRoomTypeRequest("Deluxe", $"DLX{Guid.NewGuid():N}"[..6], 2500m, 2, 3, 1, 1, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var res1 = await _client.SendAsync(r1);
        var roomType = await res1.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"CX{Guid.NewGuid():N}"[..4], null, null, "Cancel Test Room", 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var res2 = await _client.SendAsync(r2);
        var room = await res2.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest($"Cancel{Guid.NewGuid():N}"[..8], $"cx_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));
        var checkOut = checkIn.AddDays(2);

        // 1. Create + Confirm reservation
        var createReq = new CreateReservationRequest(
            propertyId, guest!.Id,
            checkIn.ToString("yyyy-MM-dd"), checkOut.ToString("yyyy-MM-dd"),
            2, 0, roomType.Id, room!.Id, 2500m, 5000m, "THB", "Phone", null, null);

        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createReq);
        var originalRes = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        using var r4c = Auth(HttpMethod.Post, $"/api/v1/reservations/{originalRes!.Id}/confirm", token, orgId);
        await _client.SendAsync(r4c);

        // 2. Check availability – room should NOT appear
        var availUrl = $"/api/v1/properties/{propertyId}/availability?checkInDate={checkIn:yyyy-MM-dd}&checkOutDate={checkOut:yyyy-MM-dd}";
        using var r5 = Auth(HttpMethod.Get, availUrl, token, orgId);
        var avail = await (await _client.SendAsync(r5)).Content.ReadFromJsonAsync<List<AvailabilityRoomTypeDto>>(_jsonOptions);
        var totalAvailable1 = avail!.Sum(rt => rt.AvailableRooms);
        Assert.Equal(0, totalAvailable1); // Room is blocked

        // 3. Cancel reservation
        using var r6 = Auth(HttpMethod.Post, $"/api/v1/reservations/{originalRes.Id}/cancel", token, orgId,
            new CancelReservationRequest("Guest changed plans"));
        var cancelRes = await (await _client.SendAsync(r6)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        Assert.Equal("Cancelled", cancelRes!.Status);

        // 4. Check availability again – room should be available now
        using var r7 = Auth(HttpMethod.Get, availUrl, token, orgId);
        var avail2 = await (await _client.SendAsync(r7)).Content.ReadFromJsonAsync<List<AvailabilityRoomTypeDto>>(_jsonOptions);
        var totalAvailable2 = avail2!.Sum(rt => rt.AvailableRooms);
        Assert.True(totalAvailable2 >= 1); // Room released

        // 5. Create a new reservation for the same dates – should succeed
        using var r8 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, createReq);
        var res8 = await _client.SendAsync(r8);
        Assert.Equal(HttpStatusCode.Created, res8.StatusCode);
    }

    [Fact]
    public async Task Phase3_ReservationInvalidTransitions_AreRejected()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Setup
        var rtReq = new CreateRoomTypeRequest("Economy", $"ECO{Guid.NewGuid():N}"[..6], 800m, 1, 2, 1, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"EC{Guid.NewGuid():N}"[..4], null, null, null, 1, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest($"Inv{Guid.NewGuid():N}"[..6], $"inv_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));

        // Create reservation (Pending)
        var resReq = new CreateReservationRequest(
            propertyId, guest!.Id,
            checkIn.ToString("yyyy-MM-dd"), checkIn.AddDays(1).ToString("yyyy-MM-dd"),
            1, 0, roomType.Id, room!.Id, 800m, 800m, "THB", "Phone", null, null);
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, resReq);
        var res = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        // Try to check-in without confirming first → must fail (422)
        using var r5 = Auth(HttpMethod.Post, $"/api/v1/reservations/{res!.Id}/check-in", token, orgId);
        var res5 = await _client.SendAsync(r5);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res5.StatusCode);

        // Confirm it
        using var r6 = Auth(HttpMethod.Post, $"/api/v1/reservations/{res.Id}/confirm", token, orgId);
        await _client.SendAsync(r6);

        // Try to confirm again → must fail (422)
        using var r7 = Auth(HttpMethod.Post, $"/api/v1/reservations/{res.Id}/confirm", token, orgId);
        var res7 = await _client.SendAsync(r7);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res7.StatusCode);
    }

    [Fact]
    public async Task Phase3_AvailabilityQuery_FiltersBlockedRooms()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Create 2 rooms of same type
        var rtReq = new CreateRoomTypeRequest("Standard", $"S{Guid.NewGuid():N}"[..7], 1000m, 2, 2, 1, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq1 = new CreateRoomRequest(roomType!.Id, $"AV1{Guid.NewGuid():N}"[..4], null, null, "Avail Room 1", 2, RoomOperationalStatus.Clean, null);
        var roomReq2 = new CreateRoomRequest(roomType.Id, $"AV2{Guid.NewGuid():N}"[..4], null, null, "Avail Room 2", 2, RoomOperationalStatus.Clean, null);

        using var r2a = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq1);
        var room1 = await (await _client.SendAsync(r2a)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);
        using var r2b = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq2);
        var room2 = await (await _client.SendAsync(r2b)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest($"Av{Guid.NewGuid():N}"[..6], $"av_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var checkOut = checkIn.AddDays(2);

        // Book room1
        var res1Req = new CreateReservationRequest(
            propertyId, guest!.Id,
            checkIn.ToString("yyyy-MM-dd"), checkOut.ToString("yyyy-MM-dd"),
            2, 0, roomType.Id, room1!.Id, 1000m, 2000m, "THB", "Phone", null, null);
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, res1Req);
        var firstRes = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);
        using var r4c = Auth(HttpMethod.Post, $"/api/v1/reservations/{firstRes!.Id}/confirm", token, orgId);
        await _client.SendAsync(r4c);

        // Query availability: only room2 should be available
        var availUrl = $"/api/v1/properties/{propertyId}/availability?checkInDate={checkIn:yyyy-MM-dd}&checkOutDate={checkOut:yyyy-MM-dd}";
        using var r5 = Auth(HttpMethod.Get, availUrl, token, orgId);
        var avail = await (await _client.SendAsync(r5)).Content.ReadFromJsonAsync<List<AvailabilityRoomTypeDto>>(_jsonOptions);

        Assert.NotNull(avail);
        var totalAvailable = avail.Sum(rt => rt.AvailableRooms);
        Assert.Equal(1, totalAvailable); // Only room2

        var availRooms = avail.SelectMany(rt => rt.Rooms).ToList();
        Assert.Contains(availRooms, r => r.RoomId == room2!.Id);
        Assert.DoesNotContain(availRooms, r => r.RoomId == room1.Id);
    }

    // -----------------------------------------------------------------
    // Double-booking prevention (application-level, sequential)
    // -----------------------------------------------------------------

    [Fact]
    public async Task Phase3_DoubleBooking_SameRoom_SameDates_SecondRequestRejected()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        var rtReq = new CreateRoomTypeRequest("DB-Room", $"DBR{Guid.NewGuid():N}"[..6], 2000m, 2, 2, 2, 1, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var rt = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(rt!.Id, $"DB{Guid.NewGuid():N}"[..6], null, null, null, 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest($"DB{Guid.NewGuid():N}"[..8], $"db_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20)).ToString("yyyy-MM-dd");
        var checkOut = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(23)).ToString("yyyy-MM-dd");

        // First booking for the same room
        var req1 = new CreateReservationRequest(propertyId, guest!.Id, checkIn, checkOut, 1, 0, rt.Id, room!.Id, 2000m, 6000m, "THB", "Phone", null, null);
        using var send1 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req1);
        var res1 = await _client.SendAsync(send1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Second booking for the same room and overlapping dates — must be rejected
        var req2 = new CreateReservationRequest(propertyId, guest.Id, checkIn, checkOut, 1, 0, rt.Id, room.Id, 2000m, 6000m, "THB", "Phone", null, null);
        using var send2 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req2);
        var res2 = await _client.SendAsync(send2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task Phase3_DoubleBooking_AdjacentDates_BothSucceed()
    {
        // A checkout on Jun 13 and checkin on Jun 13 should NOT conflict (half-open interval).
        var (token, orgId, propertyId) = await SetupAsync();

        var rtReq = new CreateRoomTypeRequest("ADJ-Room", $"ADJ{Guid.NewGuid():N}"[..6], 1500m, 1, 1, 1, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var rt = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(rt!.Id, $"ADJ{Guid.NewGuid():N}"[..6], null, null, null, 1, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest($"ADJ{Guid.NewGuid():N}"[..8], $"adj_{Guid.NewGuid():N}@test.com", null, null, null, null, null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var base1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30));
        var mid   = base1.AddDays(3);
        var end   = mid.AddDays(3);

        // Booking 1: days 30–33
        var req1 = new CreateReservationRequest(propertyId, guest!.Id,
            base1.ToString("yyyy-MM-dd"), mid.ToString("yyyy-MM-dd"),
            1, 0, rt.Id, room!.Id, 1500m, 4500m, "THB", "Phone", null, null);
        using var send1 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req1);
        var res1 = await _client.SendAsync(send1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);

        // Booking 2: days 33–36 (adjacent — should succeed)
        var req2 = new CreateReservationRequest(propertyId, guest.Id,
            mid.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"),
            1, 0, rt.Id, room.Id, 1500m, 4500m, "THB", "Phone", null, null);
        using var send2 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, req2);
        var res2 = await _client.SendAsync(send2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
    }
}

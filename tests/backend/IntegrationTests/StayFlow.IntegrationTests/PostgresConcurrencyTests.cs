using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.IntegrationTests;

/// <summary>
/// EPIC-52 — database-level guarantees, verified against real PostgreSQL.
///
/// The in-memory suite can only prove that the application-level overlap check
/// rejects a sequential second booking. These tests prove the guarantees that
/// actually protect production:
///   * the "no_room_double_booking" exclusion constraint exists and fires,
///   * genuinely parallel booking requests cannot both commit,
///   * the half-open interval is honoured by the constraint itself,
///   * cancelled / no-show reservations stop blocking inventory.
///
/// Skipped (not failed) when Docker is unavailable.
/// </summary>
[Collection("Postgres")]
public class PostgresConcurrencyTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public PostgresConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    private HttpClient NewClient() => _fixture.Factory.CreateClient();

    private static HttpRequestMessage Auth(
        HttpMethod method, string url, string token, Guid orgId, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add("X-Organization-Id", orgId.ToString());
        if (body != null) req.Content = JsonContent.Create(body);
        return req;
    }

    /// <summary>Property + room type + one room + one guest, all freshly created.</summary>
    private async Task<(string token, Guid orgId, Guid propertyId, Guid roomTypeId, Guid roomId, Guid guestId)>
        SetupSingleRoomPropertyAsync(HttpClient client)
    {
        var reg = new RegisterRequest(
            Email: $"pg_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Postgres Concurrency Manager",
            PhoneNumber: "+66900000000",
            OrganizationName: $"PG Hotels {Guid.NewGuid():N}"[..28]);

        var regRes = await client.PostAsJsonAsync("/api/v1/auth/register", reg);
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var auth = await regRes.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        var propRes = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/properties", token, orgId,
            new CreatePropertyRequest(
                Name: "PG Concurrency Hotel",
                Code: $"PGC{Guid.NewGuid():N}"[..7],
                BusinessType: PropertyType.Hotel,
                IanaTimeZone: "Asia/Bangkok",
                DefaultCurrency: "THB",
                Address: "1 Concurrency Rd",
                ContactPhone: "+66900000001",
                ContactEmail: "pg@test.com")));
        Assert.Equal(HttpStatusCode.Created, propRes.StatusCode);
        var property = await propRes.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);

        var rtRes = await client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/properties/{property!.Id}/room-types", token, orgId,
            new CreateRoomTypeRequest(
                "Deluxe", $"DLX{Guid.NewGuid():N}"[..6], 2000m, 2, 3, 2, 1, null, null)));
        var roomType = await rtRes.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomRes = await client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/properties/{property.Id}/rooms", token, orgId,
            new CreateRoomRequest(
                RoomTypeId: roomType!.Id,
                RoomNumber: $"R{Guid.NewGuid():N}"[..4],
                BuildingId: null,
                FloorId: null,
                DisplayName: "Concurrency Room",
                Capacity: 2,
                OperationalStatus: RoomOperationalStatus.Clean,
                Notes: null)));
        var room = await roomRes.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestRes = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/guests", token, orgId,
            new CreateGuestRequest(
                "Concurrency Guest", $"cg_{Guid.NewGuid():N}@test.com",
                null, null, null, null, null)));
        var guest = await guestRes.Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        return (token, orgId, property.Id, roomType.Id, room!.Id, guest!.Id);
    }

    private static CreateReservationRequest Booking(
        Guid propertyId, Guid guestId, Guid roomTypeId, Guid? roomId,
        DateOnly checkIn, DateOnly checkOut, string source = "Direct")
        => new(
            propertyId, guestId,
            checkIn.ToString("yyyy-MM-dd"), checkOut.ToString("yyyy-MM-dd"),
            2, 0, roomTypeId, roomId, 2000m, 4000m, "THB", source, null, null);

    // ── The constraint itself ────────────────────────────────────────────────

    [SkippableFact]
    public async Task ExclusionConstraint_ExistsInMigratedSchema()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM pg_constraint
            WHERE conname = 'no_room_double_booking' AND contype = 'x';
            """;
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }

    [SkippableFact]
    public async Task BtreeGistExtension_IsInstalled()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pg_extension WHERE extname = 'btree_gist';";
        var count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.Equal(1, count);
    }

    // ── Sequential and parallel double-booking ───────────────────────────────

    [SkippableFact]
    public async Task SequentialDoubleBooking_SameRoomSameDates_SecondIsRejected()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        var client = NewClient();
        var (token, orgId, propertyId, roomTypeId, roomId, guestId) =
            await SetupSingleRoomPropertyAsync(client);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(20));
        var checkOut = checkIn.AddDays(3);

        var first = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, checkIn, checkOut)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, checkIn, checkOut)));

        Assert.True(
            second.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity,
            $"Expected the second booking to be rejected, got {(int)second.StatusCode}.");
    }

    [SkippableFact]
    public async Task ParallelDoubleBooking_SameRoomSameDates_ExactlyOneCommits()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        // The guarantee that matters in production: two front-desk clerks (or a
        // clerk and an OTA webhook) hitting the same room at the same instant.
        var client = NewClient();
        var (token, orgId, propertyId, roomTypeId, roomId, guestId) =
            await SetupSingleRoomPropertyAsync(client);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(40));
        var checkOut = checkIn.AddDays(2);

        const int parallelAttempts = 8;
        var barrier = new TaskCompletionSource();

        var attempts = Enumerable.Range(0, parallelAttempts).Select(async _ =>
        {
            await barrier.Task; // release all requests together
            return await client.SendAsync(Auth(
                HttpMethod.Post, "/api/v1/reservations", token, orgId,
                Booking(propertyId, guestId, roomTypeId, roomId, checkIn, checkOut)));
        }).ToArray();

        barrier.SetResult();
        var responses = await Task.WhenAll(attempts);

        var created = responses.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(1, created);

        // And the database agrees — exactly one active row holds that room.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StayFlowDbContext>();
        var stored = await db.Reservations
            .IgnoreQueryFilters()
            .CountAsync(r => r.RoomId == roomId);

        Assert.Equal(1, stored);
    }

    // ── Half-open interval, enforced by the constraint ───────────────────────

    [SkippableFact]
    public async Task AdjacentStays_CheckoutDayEqualsNextCheckinDay_BothCommit()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        var client = NewClient();
        var (token, orgId, propertyId, roomTypeId, roomId, guestId) =
            await SetupSingleRoomPropertyAsync(client);

        var firstIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(60));
        var firstOut = firstIn.AddDays(3);

        var first = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, firstIn, firstOut)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Same room, arriving the morning the previous guest departs.
        // daterange(..., '[)') must treat this as non-overlapping.
        var second = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, firstOut, firstOut.AddDays(2))));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    // ── Cancellation releases inventory ──────────────────────────────────────

    [SkippableFact]
    public async Task CancelledReservation_StopsBlockingTheRoom()
    {
        Skip.If(_fixture.SkipReason is not null, _fixture.SkipReason);

        // The constraint's WHERE clause excludes Cancelled (5) and NoShow (6).
        // If that predicate regressed, the re-book below would hit a 23P01.
        var client = NewClient();
        var (token, orgId, propertyId, roomTypeId, roomId, guestId) =
            await SetupSingleRoomPropertyAsync(client);

        var checkIn = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(80));
        var checkOut = checkIn.AddDays(2);

        var first = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, checkIn, checkOut)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var reservation = await first.Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        var cancel = await client.SendAsync(Auth(
            HttpMethod.Post, $"/api/v1/reservations/{reservation!.Id}/cancel", token, orgId,
            new CancelReservationRequest("Guest cancelled")));
        Assert.True(cancel.IsSuccessStatusCode, $"Cancel failed: {(int)cancel.StatusCode}");

        var rebook = await client.SendAsync(Auth(
            HttpMethod.Post, "/api/v1/reservations", token, orgId,
            Booking(propertyId, guestId, roomTypeId, roomId, checkIn, checkOut)));

        Assert.Equal(HttpStatusCode.Created, rebook.StatusCode);
    }
}

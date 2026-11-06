using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

public class Phase5IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase5IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId, Guid propertyId)> SetupAsync()
    {
        var regRequest = new RegisterRequest(
            Email: $"phase5_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase5 Finance Manager",
            PhoneNumber: "+66890005555",
            OrganizationName: $"Phase5 Grand Org {Guid.NewGuid():N}"[..30]
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        var propRequest = new CreatePropertyRequest(
            Name: "Phase5 Grand Hotel & Residence",
            Code: $"P5G{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Hotel,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "Finance Ave 5, Bangkok",
            ContactPhone: "+66890006666",
            ContactEmail: "finance@phase5.com"
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
    public async Task Phase5_FolioLifecycle_AutoRoomCharges_AddCharges_Payments_CloseFolio()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // 1. Setup Room & Guest
        var rtReq = new CreateRoomTypeRequest("Deluxe", $"DLX{Guid.NewGuid():N}"[..6], 1500m, 2, 2, 2, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"501{Guid.NewGuid():N}"[..3], null, null, "Room 501", 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest("คุณสมชาย การเงิน", "somchai@billing.com", "+66819998888", null, null, "Thai", null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        // 2. Create Reservation for 2 nights @ 1,500 = 3,000 THB
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resReq = new CreateReservationRequest(
            propertyId, guest!.Id,
            today.ToString("yyyy-MM-dd"), today.AddDays(2).ToString("yyyy-MM-dd"),
            2, 0, roomType.Id, room!.Id, 1500m, 3000m, "THB", "Phone", null, null);
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, resReq);
        var res = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        // 3. Get or Create Folio -> Auto-calculated Room Charge 3,000 THB
        using var rFolio = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/reservations/{res!.Id}/folio", token, orgId);
        var folioRes = await _client.SendAsync(rFolio);
        Assert.Equal(HttpStatusCode.OK, folioRes.StatusCode);

        var folio = await folioRes.Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.NotNull(folio);
        Assert.Equal(3000m, folio.TotalCharges);
        Assert.Equal(0m, folio.TotalPayments);
        Assert.Equal(3000m, folio.Balance);
        Assert.Single(folio.Items); // Room charge item

        // 4. Add Additional Charges (Minibar 200, Food & Beverage 350)
        var charge1 = new AddChargeRequest("Minibar", "Chang Beer x 2 & Snacks", 100m, 2);
        using var rCharge1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/charges", token, orgId, charge1);
        var folioAfterCharge1 = await (await _client.SendAsync(rCharge1)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal(3200m, folioAfterCharge1!.TotalCharges);

        var charge2 = new AddChargeRequest("FoodAndBeverage", "Club Sandwich Room Service", 350m, 1);
        using var rCharge2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/charges", token, orgId, charge2);
        var folioAfterCharge2 = await (await _client.SendAsync(rCharge2)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal(3550m, folioAfterCharge2!.TotalCharges);
        Assert.Equal(3550m, folioAfterCharge2.Balance);

        // 5. Try closing folio while balance > 0 -> must fail with 422
        using var rCloseFail = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/close", token, orgId);
        var closeFailRes = await _client.SendAsync(rCloseFail);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, closeFailRes.StatusCode);

        // 6. Record Payment (PromptPay 2,000 THB)
        var pay1 = new RecordPaymentRequest("PromptPay", 2000m, "TXN-PP-001", "Scan QR at Front Desk");
        using var rPay1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/payments", token, orgId, pay1);
        var folioAfterPay1 = await (await _client.SendAsync(rPay1)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal(2000m, folioAfterPay1!.TotalPayments);
        Assert.Equal(1550m, folioAfterPay1.Balance);

        // 7. Record Remaining Payment (Cash 1,550 THB) -> Balance = 0
        var pay2 = new RecordPaymentRequest("Cash", 1550m, null, "Cash payment upon checkout");
        using var rPay2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/payments", token, orgId, pay2);
        var folioSettled = await (await _client.SendAsync(rPay2)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal(3550m, folioSettled!.TotalPayments);
        Assert.Equal(0m, folioSettled.Balance);

        // 8. Close Folio -> must succeed
        using var rCloseSuccess = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/close", token, orgId);
        var closeSuccessRes = await _client.SendAsync(rCloseSuccess);
        Assert.Equal(HttpStatusCode.OK, closeSuccessRes.StatusCode);
        var finalFolio = await closeSuccessRes.Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal("Closed", finalFolio!.Status);
        Assert.NotNull(finalFolio.ClosedAtUtc);
    }

    [Fact]
    public async Task Phase5_PaymentReversal_RestoresBalance_And_AuditSafe()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // Create Room & Guest
        var rtReq = new CreateRoomTypeRequest("Standard", $"STD{Guid.NewGuid():N}"[..6], 1000m, 2, 2, 2, 0, null, null);
        using var r1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/room-types", token, orgId, rtReq);
        var roomType = await (await _client.SendAsync(r1)).Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        var roomReq = new CreateRoomRequest(roomType!.Id, $"REV{Guid.NewGuid():N}"[..3], null, null, "Rev Room", 2, RoomOperationalStatus.Clean, null);
        using var r2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/rooms", token, orgId, roomReq);
        var room = await (await _client.SendAsync(r2)).Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        var guestReq = new CreateGuestRequest("แขก ทดสอบคืนเงิน", "reversal@test.com", null, null, null, "Thai", null);
        using var r3 = Auth(HttpMethod.Post, "/api/v1/guests", token, orgId, guestReq);
        var guest = await (await _client.SendAsync(r3)).Content.ReadFromJsonAsync<GuestDto>(_jsonOptions);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var resReq = new CreateReservationRequest(
            propertyId, guest!.Id,
            today.ToString("yyyy-MM-dd"), today.AddDays(1).ToString("yyyy-MM-dd"),
            1, 0, roomType.Id, room!.Id, 1000m, 1000m, "THB", "Phone", null, null);
        using var r4 = Auth(HttpMethod.Post, "/api/v1/reservations", token, orgId, resReq);
        var res = await (await _client.SendAsync(r4)).Content.ReadFromJsonAsync<ReservationDto>(_jsonOptions);

        // Get Folio -> 1,000 THB room charge
        using var rFolio = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/reservations/{res!.Id}/folio", token, orgId);
        var folio = await (await _client.SendAsync(rFolio)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);

        // 1. Record Payment (CreditCard 1,000 THB) -> Balance = 0
        var payReq = new RecordPaymentRequest("CreditCard", 1000m, "EDC-5544", "Credit card pre-auth");
        using var rPay = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio!.Id}/payments", token, orgId, payReq);
        var folioPaid = await (await _client.SendAsync(rPay)).Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        Assert.Equal(0m, folioPaid!.Balance);
        var paymentId = folioPaid.Payments.First().Id;

        // 2. Reverse Payment (Card voided/refunded)
        var revReq = new ReversePaymentRequest("Customer card swiped twice by mistake");
        using var rRev = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/folios/{folio.Id}/payments/{paymentId}/reverse", token, orgId, revReq);
        var revRes = await _client.SendAsync(rRev);
        Assert.Equal(HttpStatusCode.OK, revRes.StatusCode);

        var folioAfterRev = await revRes.Content.ReadFromJsonAsync<FolioDto>(_jsonOptions);
        // Balance restored back to 1,000 THB
        Assert.Equal(1000m, folioAfterRev!.Balance);
        Assert.Equal(0m, folioAfterRev.TotalPayments); // Reversed payment is not counted
        var reversedPayment = folioAfterRev.Payments.First(p => p.Id == paymentId);
        Assert.Equal("Reversed", reversedPayment.Status);
        Assert.Equal("Customer card swiped twice by mistake", reversedPayment.ReversalReason);
    }

    [Fact]
    public async Task Phase5_Reporting_DailyTransactions_And_Occupancy_And_Revenue()
    {
        var (token, orgId, propertyId) = await SetupAsync();

        // 1. Daily Transactions Query
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        using var rDaily = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/reports/daily-transactions?date={today}", token, orgId);
        var resDaily = await _client.SendAsync(rDaily);
        Assert.Equal(HttpStatusCode.OK, resDaily.StatusCode);
        var dailySummary = await resDaily.Content.ReadFromJsonAsync<DailyTransactionSummaryDto>(_jsonOptions);
        Assert.NotNull(dailySummary);
        Assert.NotNull(dailySummary.AmountByMethod);

        // 2. Occupancy Report Query
        using var rOcc = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/reports/occupancy?startDate={today}&endDate={today}", token, orgId);
        var resOcc = await _client.SendAsync(rOcc);
        Assert.Equal(HttpStatusCode.OK, resOcc.StatusCode);
        var occReport = await resOcc.Content.ReadFromJsonAsync<OccupancyReportDto>(_jsonOptions);
        Assert.NotNull(occReport);
        Assert.Single(occReport.DailyBreakdown);

        // 3. Revenue Report Query
        using var rRev = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/reports/revenue", token, orgId);
        var resRev = await _client.SendAsync(rRev);
        Assert.Equal(HttpStatusCode.OK, resRev.StatusCode);
        var revReport = await resRev.Content.ReadFromJsonAsync<RevenueReportDto>(_jsonOptions);
        Assert.NotNull(revReport);
        Assert.NotNull(revReport.Categories);
    }
}

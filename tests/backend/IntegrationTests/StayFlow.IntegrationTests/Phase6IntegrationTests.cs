using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using StayFlow.Api.Endpoints;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;

namespace StayFlow.IntegrationTests;

public class Phase6IntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public Phase6IntegrationTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string token, Guid orgId, Guid propertyId, Guid roomId)> SetupApartmentAsync()
    {
        var regRequest = new RegisterRequest(
            Email: $"phase6_{Guid.NewGuid():N}@test.com",
            Password: "TestPassword123!",
            FullName: "Phase6 Apartment Manager",
            PhoneNumber: "+66890007777",
            OrganizationName: $"Phase6 Apartment Group {Guid.NewGuid():N}"[..30]
        );
        var regResponse = await _client.PostAsJsonAsync("/api/v1/auth/register", regRequest);
        var auth = await regResponse.Content.ReadFromJsonAsync<AuthResponse>(_jsonOptions);
        var token = auth!.AccessToken;
        var orgId = auth.ActiveOrganizationId!.Value;

        var propRequest = new CreatePropertyRequest(
            Name: "StayFlow Resident Mansion",
            Code: $"MAN{Guid.NewGuid():N}"[..7],
            BusinessType: PropertyType.Apartment,
            IanaTimeZone: "Asia/Bangkok",
            DefaultCurrency: "THB",
            Address: "123 Ratchada, Bangkok",
            ContactPhone: "+66890008888",
            ContactEmail: "mansion@stayflow.com"
        );
        using var reqProp = new HttpRequestMessage(HttpMethod.Post, "/api/v1/properties");
        reqProp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        reqProp.Headers.Add("X-Organization-Id", orgId.ToString());
        reqProp.Content = JsonContent.Create(propRequest);
        var resProp = await _client.SendAsync(reqProp);
        var prop = await resProp.Content.ReadFromJsonAsync<PropertyDto>(_jsonOptions);

        // Create RoomType
        var rtReq = new CreateRoomTypeRequest(
            Name: "Studio Standard Room",
            Code: "STD",
            BaseRate: 6000m,
            StandardOccupancy: 2,
            MaxOccupancy: 2,
            MaxAdults: 2,
            MaxChildren: 0,
            Description: "Standard 30 sqm rental room",
            AmenitiesJson: null
        );
        using var reqRt = Auth(HttpMethod.Post, $"/api/v1/properties/{prop.Id}/room-types", token, orgId, rtReq);
        var resRt = await _client.SendAsync(reqRt);
        var rt = await resRt.Content.ReadFromJsonAsync<RoomTypeDto>(_jsonOptions);

        // Create Room 301
        var roomReq = new CreateRoomRequest(
            RoomTypeId: rt!.Id,
            RoomNumber: "301",
            BuildingId: null,
            FloorId: null,
            DisplayName: "Room 301 (3rd Floor)",
            Capacity: 2,
            OperationalStatus: RoomOperationalStatus.Clean,
            Notes: "Monthly rental unit"
        );
        using var reqRoom = Auth(HttpMethod.Post, $"/api/v1/properties/{prop.Id}/rooms", token, orgId, roomReq);
        var resRoom = await _client.SendAsync(reqRoom);
        var room = await resRoom.Content.ReadFromJsonAsync<RoomDto>(_jsonOptions);

        return (token, orgId, prop.Id, room!.Id);
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
    public async Task Phase6_TenantAndAgreementLifecycle_OverlapConflictPrevention()
    {
        var (token, orgId, propertyId, roomId) = await SetupApartmentAsync();

        // 1. Create Tenant
        var tenantReq = new CreateTenantRequest(
            FullName: "สมชาย รักสงบ (Tenant)",
            NationalIdOrPassport: "1100200300400",
            PhoneNumber: "0812345678",
            Email: "somchai@tenant.com",
            EmergencyContactName: "สมศรี รักสงบ",
            EmergencyContactPhone: "0819998888"
        );
        using var reqTenant = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/tenants", token, orgId, tenantReq);
        var resTenant = await _client.SendAsync(reqTenant);
        Assert.Equal(HttpStatusCode.Created, resTenant.StatusCode);
        var tenant = await resTenant.Content.ReadFromJsonAsync<TenantDto>(_jsonOptions);
        Assert.NotNull(tenant);

        // 2. Create Rental Agreement (2026-11-01 to 2027-04-30)
        var agreeReq = new CreateAgreementRequest(
            AgreementNumber: "AGR-301-2026",
            TenantId: tenant.Id,
            RoomId: roomId,
            StartDate: new DateOnly(2026, 11, 1),
            EndDate: new DateOnly(2027, 4, 30),
            MonthlyRent: 6000m,
            SecurityDeposit: 12000m,
            BillingCycleDay: 25,
            Notes: "6 months standard lease"
        );
        using var reqAgree = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/agreements", token, orgId, agreeReq);
        var resAgree = await _client.SendAsync(reqAgree);
        Assert.Equal(HttpStatusCode.Created, resAgree.StatusCode);
        var agree = await resAgree.Content.ReadFromJsonAsync<RentalAgreementDto>(_jsonOptions);
        Assert.NotNull(agree);
        Assert.Equal("Draft", agree.Status);

        // 3. Record Deposit Payment
        var depReq = new RecordDepositRequest(12000m);
        using var reqDep = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree.Id}/deposit", token, orgId, depReq);
        var resDep = await _client.SendAsync(reqDep);
        Assert.Equal(HttpStatusCode.OK, resDep.StatusCode);

        // 4. Activate Agreement
        using var reqAct = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree.Id}/activate", token, orgId);
        var resAct = await _client.SendAsync(reqAct);
        Assert.Equal(HttpStatusCode.OK, resAct.StatusCode);

        // 5. Test Overlap Conflict Prevention:
        // Try creating another agreement for the same room during overlapping period (2026-12-01 to 2027-02-28)
        var overlapReq = new CreateAgreementRequest(
            AgreementNumber: "AGR-301-OVERLAP",
            TenantId: tenant.Id,
            RoomId: roomId,
            StartDate: new DateOnly(2026, 12, 1),
            EndDate: new DateOnly(2027, 2, 28),
            MonthlyRent: 6500m,
            SecurityDeposit: 13000m,
            BillingCycleDay: 25,
            Notes: "This should fail due to active agreement conflict"
        );
        using var reqOverlap = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/agreements", token, orgId, overlapReq);
        var resOverlap = await _client.SendAsync(reqOverlap);
        Assert.Equal(HttpStatusCode.Conflict, resOverlap.StatusCode);
    }

    [Fact]
    public async Task Phase6_MeterReadings_And_MonthlyInvoicing_Idempotency()
    {
        var (token, orgId, propertyId, roomId) = await SetupApartmentAsync();

        // Setup tenant & active agreement
        var tenantReq = new CreateTenantRequest("มานะ อดทน", "1234567890123", "0891112222", null, null, null);
        using var reqTenant = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/tenants", token, orgId, tenantReq);
        var resTenant = await _client.SendAsync(reqTenant);
        var tenant = await resTenant.Content.ReadFromJsonAsync<TenantDto>(_jsonOptions);

        var agreeReq = new CreateAgreementRequest(
            AgreementNumber: "AGR-MANA-301",
            TenantId: tenant!.Id,
            RoomId: roomId,
            StartDate: new DateOnly(2026, 11, 1),
            EndDate: new DateOnly(2027, 10, 31),
            MonthlyRent: 6000m,
            SecurityDeposit: 12000m,
            BillingCycleDay: 25,
            Notes: "1 year lease"
        );
        using var reqAgree = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/agreements", token, orgId, agreeReq);
        var resAgree = await _client.SendAsync(reqAgree);
        var agree = await resAgree.Content.ReadFromJsonAsync<RentalAgreementDto>(_jsonOptions);

        // Activate
        using var reqAct = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree!.Id}/activate", token, orgId);
        await _client.SendAsync(reqAct);

        // 1. Record Electricity Meter (100 -> 150 = 50 units @ 7.00 THB = 350.00 THB)
        var elecReq = new RecordMeterReadingRequest(
            RoomId: roomId,
            UtilityType: "Electricity",
            ReadingDate: new DateOnly(2026, 11, 25),
            BillingMonth: "2026-11",
            PreviousReading: 100m,
            CurrentReading: 150m,
            UnitPrice: 7.00m,
            Notes: "มิเตอร์ไฟประจำเดือน พ.ย."
        );
        using var reqElec = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/meter-readings", token, orgId, elecReq);
        var resElec = await _client.SendAsync(reqElec);
        Assert.Equal(HttpStatusCode.Created, resElec.StatusCode);
        var elec = await resElec.Content.ReadFromJsonAsync<MeterReadingDto>(_jsonOptions);
        Assert.Equal(50m, elec!.UnitsUsed);
        Assert.Equal(350.00m, elec.TotalAmount);

        // 2. Record Water Meter (20 -> 30 = 10 units @ 18.00 THB = 180.00 THB)
        var waterReq = new RecordMeterReadingRequest(
            RoomId: roomId,
            UtilityType: "Water",
            ReadingDate: new DateOnly(2026, 11, 25),
            BillingMonth: "2026-11",
            PreviousReading: 20m,
            CurrentReading: 30m,
            UnitPrice: 18.00m,
            Notes: "มิเตอร์น้ำประจำเดือน พ.ย."
        );
        using var reqWater = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/meter-readings", token, orgId, waterReq);
        var resWater = await _client.SendAsync(reqWater);
        Assert.Equal(HttpStatusCode.Created, resWater.StatusCode);
        var water = await resWater.Content.ReadFromJsonAsync<MeterReadingDto>(_jsonOptions);
        Assert.Equal(10m, water!.UnitsUsed);
        Assert.Equal(180.00m, water.TotalAmount);

        // 3. Generate Monthly Invoices for "2026-11"
        var genReq = new GenerateMonthlyInvoicesRequest(
            BillingMonth: "2026-11",
            DueDate: new DateOnly(2026, 12, 5)
        );
        using var reqGen1 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/invoices/generate-monthly", token, orgId, genReq);
        var resGen1 = await _client.SendAsync(reqGen1);
        Assert.Equal(HttpStatusCode.OK, resGen1.StatusCode);
        var summary1 = await resGen1.Content.ReadFromJsonAsync<GenerateInvoicesSummaryDto>(_jsonOptions);
        Assert.NotNull(summary1);
        Assert.Equal(1, summary1.GeneratedCount);
        Assert.Equal(0, summary1.SkippedCount);

        var invoice = summary1.Invoices.First();
        // Rent (6000) + Elec (350) + Water (180) = 6530.00
        Assert.Equal(6530.00m, invoice.TotalAmount);
        Assert.Equal(3, invoice.Items.Count);
        Assert.Equal("Issued", invoice.Status);

        // 4. Idempotency Check: Running invoice generation again must NOT create duplicate invoices
        using var reqGen2 = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/invoices/generate-monthly", token, orgId, genReq);
        var resGen2 = await _client.SendAsync(reqGen2);
        Assert.Equal(HttpStatusCode.OK, resGen2.StatusCode);
        var summary2 = await resGen2.Content.ReadFromJsonAsync<GenerateInvoicesSummaryDto>(_jsonOptions);
        Assert.NotNull(summary2);
        Assert.Equal(0, summary2.GeneratedCount);
        Assert.Equal(1, summary2.SkippedCount); // Correctly skipped due to idempotency!

        // 5. Record Payment for Monthly Invoice
        var payReq = new RecordInvoicePaymentRequest(
            Amount: 6530.00m,
            Method: "PromptPay",
            ReferenceNumber: "PP-202611-9988",
            Notes: "ชำระครบเต็มจำนวนผ่านพร้อมเพย์"
        );
        using var reqPay = Auth(HttpMethod.Post, $"/api/v1/invoices/{invoice.Id}/payments", token, orgId, payReq);
        var resPay = await _client.SendAsync(reqPay);
        Assert.Equal(HttpStatusCode.OK, resPay.StatusCode);

        // Verify Invoice state
        using var reqGetInv = Auth(HttpMethod.Get, $"/api/v1/invoices/{invoice.Id}", token, orgId);
        var resGetInv = await _client.SendAsync(reqGetInv);
        var finalInv = await resGetInv.Content.ReadFromJsonAsync<MonthlyInvoiceDto>(_jsonOptions);
        Assert.Equal("Paid", finalInv!.Status);
        Assert.Equal(0m, finalInv.Balance);
        Assert.Single(finalInv.Payments);
    }

    [Fact]
    public async Task Phase6_MoveOutSettlement_DepositReconciliation_RoomTurnoverToDirty()
    {
        var (token, orgId, propertyId, roomId) = await SetupApartmentAsync();

        // 1. Setup tenant, agreement, deposit and activate
        var tenantReq = new CreateTenantRequest("วิชัย ชื่นใจ", "3456789012345", "0815556666", null, null, null);
        using var reqTenant = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/tenants", token, orgId, tenantReq);
        var resTenant = await _client.SendAsync(reqTenant);
        var tenant = await resTenant.Content.ReadFromJsonAsync<TenantDto>(_jsonOptions);

        var agreeReq = new CreateAgreementRequest(
            AgreementNumber: "AGR-WICHAI-301",
            TenantId: tenant!.Id,
            RoomId: roomId,
            StartDate: new DateOnly(2026, 5, 1),
            EndDate: new DateOnly(2026, 10, 31),
            MonthlyRent: 5500m,
            SecurityDeposit: 11000m,
            BillingCycleDay: 25,
            Notes: "6 months lease"
        );
        using var reqAgree = Auth(HttpMethod.Post, $"/api/v1/properties/{propertyId}/agreements", token, orgId, agreeReq);
        var resAgree = await _client.SendAsync(reqAgree);
        var agree = await resAgree.Content.ReadFromJsonAsync<RentalAgreementDto>(_jsonOptions);

        // Record deposit
        using var reqDep = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree!.Id}/deposit", token, orgId, new RecordDepositRequest(11000m));
        await _client.SendAsync(reqDep);

        // Activate
        using var reqAct = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree.Id}/activate", token, orgId);
        await _client.SendAsync(reqAct);

        // Verify room is currently clean or active
        using var reqRoomBefore = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/rooms", token, orgId);
        var resRoomBefore = await _client.SendAsync(reqRoomBefore);
        var roomsBefore = await resRoomBefore.Content.ReadFromJsonAsync<List<RoomDto>>(_jsonOptions);
        var roomBefore = roomsBefore!.First(r => r.Id == roomId);

        // 2. Terminate with Move-Out Settlement:
        // Deposit Paid: 11,000 THB
        // Deduct 800 THB (Wall repair & room cleaning)
        // Refund 10,200 THB to tenant
        var termReq = new TerminateAgreementRequest(
            Reason: "ครบกำหนดสัญญาและย้ายออก (Contract expired)",
            RefundAmount: 10200m,
            DeductionAmount: 800m
        );
        using var reqTerm = Auth(HttpMethod.Post, $"/api/v1/agreements/{agree.Id}/terminate", token, orgId, termReq);
        var resTerm = await _client.SendAsync(reqTerm);
        Assert.Equal(HttpStatusCode.OK, resTerm.StatusCode);

        // 3. Verify Agreement is Terminated
        using var reqGetAgree = Auth(HttpMethod.Get, $"/api/v1/agreements/{agree.Id}", token, orgId);
        var resGetAgree = await _client.SendAsync(reqGetAgree);
        var finalAgree = await resGetAgree.Content.ReadFromJsonAsync<RentalAgreementDto>(_jsonOptions);
        Assert.Equal("Terminated", finalAgree!.Status);
        Assert.Equal(10200m, finalAgree.DepositRefundedAmount);
        Assert.Equal(800m, finalAgree.DepositDeductedAmount);

        // 4. Verify Room Operational Status was automatically changed to 'Dirty' for housekeeping turnover!
        using var reqRoomAfter = Auth(HttpMethod.Get, $"/api/v1/properties/{propertyId}/rooms", token, orgId);
        var resRoomAfter = await _client.SendAsync(reqRoomAfter);
        var roomsAfter = await resRoomAfter.Content.ReadFromJsonAsync<List<RoomDto>>(_jsonOptions);
        var roomAfter = roomsAfter!.First(r => r.Id == roomId);
        Assert.Equal("Dirty", roomAfter.OperationalStatus);
    }
}

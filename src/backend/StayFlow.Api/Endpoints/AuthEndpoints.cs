using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Email;
using StayFlow.BuildingBlocks.Domain.Apartments;
using StayFlow.BuildingBlocks.Domain.Authorization;
using StayFlow.BuildingBlocks.Deployment;
using StayFlow.BuildingBlocks.Entitlements;
using StayFlow.BuildingBlocks.Domain.Identity;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Parties;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.MultiTenancy;
using StayFlow.BuildingBlocks.Persistence;
using StayFlow.BuildingBlocks.Security;

namespace StayFlow.Api.Endpoints;

public record RegisterRequest(
    string Email,
    string Password,
    string FullName,
    string? PhoneNumber,
    string? OrganizationName
);

public record LoginRequest(
    string Email,
    string Password,
    Guid? OrganizationId
);

public record RefreshTokenRequest(
    string RefreshToken
);

public record UserDto(
    Guid Id,
    string Email,
    string FullName,
    string? PhoneNumber,
    string PreferredLanguage
);

public record UpdateLanguageRequest(
    string Language
);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string NewPassword);

public record UserOrganizationDto(
    Guid OrganizationId,
    string OrganizationName,
    string OrganizationSlug,
    string Role
);

public record AuthResponse(
    string AccessToken,
    string RefreshToken,
    UserDto User,
    Guid? ActiveOrganizationId,
    string? ActiveRole,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<UserOrganizationDto> Organizations
);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/register", RegisterAsync)
            .WithName("Register")
            .WithSummary("Register a new user account and optionally create an organization")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapPost("/login", LoginAsync)
            .WithName("Login")
            .WithSummary("Authenticate with email and password")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .AllowAnonymous();

        group.MapPost("/refresh", RefreshAsync)
            .WithName("RefreshToken")
            .WithSummary("Refresh an expired access token using a refresh token")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        group.MapPost("/quick-demo", QuickDemoAsync)
            .WithName("QuickDemo")
            .WithSummary("One-click initialization with demo user, organization, property, and sample rooms")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .AllowAnonymous();

        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .WithSummary("Get profile and organizations of the authenticated user")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        group.MapPut("/me/language", UpdateLanguageAsync)
            .WithName("UpdatePreferredLanguage")
            .WithSummary("Update the authenticated user's preferred UI language")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        group.MapPost("/forgot-password", ForgotPasswordAsync)
            .WithName("ForgotPassword")
            .WithSummary("Request a password-reset link by email (always returns 200 to prevent email enumeration)")
            .Produces(StatusCodes.Status200OK)
            .AllowAnonymous();

        group.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetPassword")
            .WithSummary("Set a new password using a valid reset token")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .AllowAnonymous();

        return group;
    }

    private static async Task<IResult> UpdateLanguageAsync(
        [FromBody] UpdateLanguageRequest request,
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        // A user may only change their own language; the identity comes from the
        // token, never from the request body.
        var user = await dbContext.Users.FindAsync([tenantContext.UserId.Value], ct);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        if (!SupportedLanguages.IsSupported(request.Language))
        {
            return Results.Problem(
                detail: $"Unsupported language. Supported values: {string.Join(", ", SupportedLanguages.All)}.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Unsupported language");
        }

        user.SetPreferredLanguage(request.Language);
        await dbContext.SaveChangesAsync(ct);

        return Results.Ok(new UserDto(
            user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage));
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterRequest request,
        StayFlowDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        Microsoft.Extensions.Options.IOptions<DeploymentOptions> deploymentOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem("Email and password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        if (!System.Net.Mail.MailAddress.TryCreate(normalizedEmail, out _))
        {
            return Results.Problem("The email address is not valid.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Password.Length < 8)
        {
            return Results.Problem("Password must be at least 8 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!request.Password.Any(char.IsLetter) || !request.Password.Any(char.IsDigit))
        {
            return Results.Problem("Password must contain at least one letter and one digit.", statusCode: StatusCodes.Status400BadRequest);
        }
        var existingUser = await dbContext.Users
            .AnyAsync(u => u.Email == normalizedEmail, ct);

        if (existingUser)
        {
            return Results.Problem("A user with this email address already exists.", statusCode: StatusCodes.Status400BadRequest);
        }

        var passwordHash = passwordHasher.HashPassword(request.Password);
        var user = User.Create(normalizedEmail, passwordHash, request.FullName, request.PhoneNumber);
        dbContext.Users.Add(user);

        Organization? org = null;
        OrganizationMembership? membership = null;

        if (!string.IsNullOrWhiteSpace(request.OrganizationName))
        {
            org = Organization.Create(request.OrganizationName);
            dbContext.Organizations.Add(org);

            membership = OrganizationMembership.Create(org.Id, user.Id, OrganizationRole.Owner);
            dbContext.OrganizationMemberships.Add(membership);

            await SubscriptionProvisioning.StartTrialForNewOrganizationAsync(
                dbContext, deploymentOptions.Value, org.Id, user.Id, ct);
        }

        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, rawRefreshToken, TimeSpan.FromDays(30));
        dbContext.RefreshTokens.Add(refreshToken);

        await dbContext.SaveChangesAsync(ct);

        var permissions = membership != null ? Permissions.GetPermissionsForRole(membership.Role) : [];
        var accessToken = tokenService.GenerateAccessToken(user, org?.Id, membership?.Role, permissions);

        var orgList = org != null
            ? new List<UserOrganizationDto> { new(org.Id, org.Name, org.Slug, membership!.Role.ToString()) }
            : new List<UserOrganizationDto>();

        return Results.Ok(new AuthResponse(
            accessToken,
            rawRefreshToken,
            new UserDto(user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage),
            org?.Id,
            membership?.Role.ToString(),
            permissions,
            orgList
        ));
    }

    private static async Task<IResult> LoginAsync(
        [FromBody] LoginRequest request,
        StayFlowDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive, ct);

        if (user == null || !passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
        }

        // Load user memberships and organizations
        var memberships = await dbContext.OrganizationMemberships
            .Where(m => m.UserId == user.Id)
            .Join(dbContext.Organizations.Where(o => o.IsActive),
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => new { Membership = m, Organization = o })
            .ToListAsync(ct);

        OrganizationMembership? activeMembership = null;
        Organization? activeOrg = null;

        if (request.OrganizationId.HasValue)
        {
            var selected = memberships.FirstOrDefault(x => x.Organization.Id == request.OrganizationId.Value);
            activeMembership = selected?.Membership;
            activeOrg = selected?.Organization;
        }
        else
        {
            var first = memberships.FirstOrDefault();
            activeMembership = first?.Membership;
            activeOrg = first?.Organization;
        }

        var permissions = activeMembership != null ? Permissions.GetPermissionsForRole(activeMembership.Role) : [];
        var accessToken = tokenService.GenerateAccessToken(user, activeOrg?.Id, activeMembership?.Role, permissions);

        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, rawRefreshToken, TimeSpan.FromDays(30));
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(ct);

        var orgDtos = memberships.Select(x => new UserOrganizationDto(
            x.Organization.Id,
            x.Organization.Name,
            x.Organization.Slug,
            x.Membership.Role.ToString()
        )).ToList();

        return Results.Ok(new AuthResponse(
            accessToken,
            rawRefreshToken,
            new UserDto(user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage),
            activeOrg?.Id,
            activeMembership?.Role.ToString(),
            permissions,
            orgDtos
        ));
    }

    private static async Task<IResult> RefreshAsync(
        [FromBody] RefreshTokenRequest request,
        StayFlowDbContext dbContext,
        ITokenService tokenService,
        CancellationToken ct)
    {
        var tokenRecord = await dbContext.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == request.RefreshToken, ct);

        if (tokenRecord == null || !tokenRecord.IsActive)
        {
            return Results.Problem("Invalid or expired refresh token.", statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await dbContext.Users.FindAsync([tokenRecord.UserId], ct);
        if (user == null || !user.IsActive)
        {
            return Results.Problem("User is inactive or not found.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Revoke used token and issue new one (rotation)
        tokenRecord.Revoke();
        var newRawRefreshToken = tokenService.GenerateRefreshToken();
        var newRefreshToken = RefreshToken.Create(user.Id, newRawRefreshToken, TimeSpan.FromDays(30));
        dbContext.RefreshTokens.Add(newRefreshToken);

        var memberships = await dbContext.OrganizationMemberships
            .Where(m => m.UserId == user.Id)
            .Join(dbContext.Organizations.Where(o => o.IsActive),
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => new { Membership = m, Organization = o })
            .ToListAsync(ct);

        var first = memberships.FirstOrDefault();
        var permissions = first != null ? Permissions.GetPermissionsForRole(first.Membership.Role) : [];
        var accessToken = tokenService.GenerateAccessToken(user, first?.Organization.Id, first?.Membership.Role, permissions);

        await dbContext.SaveChangesAsync(ct);

        var orgDtos = memberships.Select(x => new UserOrganizationDto(
            x.Organization.Id,
            x.Organization.Name,
            x.Organization.Slug,
            x.Membership.Role.ToString()
        )).ToList();

        return Results.Ok(new AuthResponse(
            accessToken,
            newRawRefreshToken,
            new UserDto(user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage),
            first?.Organization.Id,
            first?.Membership.Role.ToString(),
            permissions,
            orgDtos
        ));
    }

    private static async Task<IResult> QuickDemoAsync(
        StayFlowDbContext dbContext,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        Microsoft.Extensions.Options.IOptions<DeploymentOptions> deploymentOptions,
        CancellationToken ct)
    {
        const string demoEmail = "demo@stayflow.com";

        // 1. Get or create demo user
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Email == demoEmail, ct);
        if (user == null)
        {
            var passHash = passwordHasher.HashPassword("StayFlowDemo2026!");
            user = User.Create(demoEmail, passHash, "โรงแรมแกรนด์สยาม รีสอร์ท (ผู้จัดการ)");
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(ct);
        }

        // 2. Get or create demo organization
        var org = await dbContext.Organizations.FirstOrDefaultAsync(o => o.Slug == "stayflow-grand-resort", ct);
        OrganizationMembership? membership = null;

        if (org == null)
        {
            org = Organization.Create("StayFlow Grand Resort Group", "stayflow-grand-resort");
            dbContext.Organizations.Add(org);
            await dbContext.SaveChangesAsync(ct);

            membership = OrganizationMembership.Create(org.Id, user.Id, OrganizationRole.Owner);
            dbContext.OrganizationMemberships.Add(membership);

            await SubscriptionProvisioning.StartTrialForNewOrganizationAsync(
                dbContext, deploymentOptions.Value, org.Id, user.Id, ct);
            await dbContext.SaveChangesAsync(ct);
        }
        else
        {
            membership = await dbContext.OrganizationMemberships
                .FirstOrDefaultAsync(m => m.OrganizationId == org.Id && m.UserId == user.Id, ct);

            if (membership == null)
            {
                membership = OrganizationMembership.Create(org.Id, user.Id, OrganizationRole.Owner);
                dbContext.OrganizationMemberships.Add(membership);
                await dbContext.SaveChangesAsync(ct);
            }
        }

        // 3. Get or create demo property
        var property = await dbContext.Properties
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.OrganizationId == org.Id && p.Code == "SFG-01", ct);

        if (property == null)
        {
            property = Property.Create(
                organizationId: org.Id,
                name: "StayFlow Grand Hotel & Suites",
                code: "SFG-01",
                businessType: PropertyType.Hotel,
                ianaTimeZone: "Asia/Bangkok",
                defaultCurrency: "THB",
                address: "สุขุมวิท 21, อโศก, กรุงเทพฯ 10110",
                contactPhone: "+6621234567",
                contactEmail: "hotel@stayflow.com"
            );
            dbContext.Properties.Add(property);
            await dbContext.SaveChangesAsync(ct);

            // Create building & floors
            var bld = Building.Create(org.Id, property.Id, "อาคารหลัก (Main Wing)", "MW", 3);
            dbContext.Buildings.Add(bld);
            await dbContext.SaveChangesAsync(ct);

            var floor1 = Floor.Create(org.Id, property.Id, bld.Id, 1, "ชั้น 1");
            var floor2 = Floor.Create(org.Id, property.Id, bld.Id, 2, "ชั้น 2");
            dbContext.Floors.AddRange(floor1, floor2);
            await dbContext.SaveChangesAsync(ct);

            // Create room types
            var rtDeluxe = RoomType.Create(org.Id, property.Id, "Deluxe King Bed", "DLX-K", 1800m, 2, 3, 2, 1, "ห้องพักเตียงเดี่ยวขนาดคิงไซส์ วิวสระว่ายน้ำ");
            var rtSuperior = RoomType.Create(org.Id, property.Id, "Superior Twin Bed", "SUP-T", 1400m, 2, 2, 2, 0, "ห้องพักเตียงคู่สำหรับ 2 ท่าน");
            var rtSuite = RoomType.Create(org.Id, property.Id, "Executive Suite", "EXE-S", 3200m, 3, 4, 3, 2, "ห้องชุดสวีทสุดหรูพร้อมห้องนั่งเล่นและอ่างอาบน้ำ");

            dbContext.RoomTypes.AddRange(rtDeluxe, rtSuperior, rtSuite);
            await dbContext.SaveChangesAsync(ct);

            // Create starter rooms
            var r101 = Room.Create(org.Id, property.Id, rtDeluxe.Id, "101", bld.Id, floor1.Id, "Deluxe King 101", 2, RoomOperationalStatus.Clean, "ใกล้สระว่ายน้ำ");
            var r102 = Room.Create(org.Id, property.Id, rtDeluxe.Id, "102", bld.Id, floor1.Id, "Deluxe King 102", 2, RoomOperationalStatus.Dirty, "ลูกค้าเพิ่งเช็คเอาท์");
            var r103 = Room.Create(org.Id, property.Id, rtSuperior.Id, "103", bld.Id, floor1.Id, "Superior Twin 103", 2, RoomOperationalStatus.Cleaning, "แม่บ้านกำลังทำความสะอาด");
            var r201 = Room.Create(org.Id, property.Id, rtSuite.Id, "201", bld.Id, floor2.Id, "Executive Suite 201", 3, RoomOperationalStatus.Inspected, "หัวหน้าแม่บ้านตรวจแล้ว พร้อมต้อนรับ");
            var r202 = Room.Create(org.Id, property.Id, rtSuperior.Id, "202", bld.Id, floor2.Id, "Superior Twin 202", 2, RoomOperationalStatus.Clean, "วิวสวน");
            var r203 = Room.Create(org.Id, property.Id, rtDeluxe.Id, "203", bld.Id, floor2.Id, "Deluxe King 203", 2, RoomOperationalStatus.Maintenance, "ปิดซ่อมท่อน้ำ");

            dbContext.Rooms.AddRange(r101, r102, r103, r201, r202, r203);
            await dbContext.SaveChangesAsync(ct);
        }

        // 4. Seed demo guests and reservations if none exist for this property
        var hasGuests = await dbContext.Guests
            .IgnoreQueryFilters()
            .AnyAsync(g => g.OrganizationId == org.Id, ct);

        if (!hasGuests)
        {
            var g1 = Guest.Create(org.Id, "คุณวิชัย มณีวรรณ", "wichai.m@gmail.com", "+66812345678", "1100500123456", null, "Thai", "VIP Guest ประจำโรงแรม");
            var g2 = Guest.Create(org.Id, "Ms. Emma Watson", "emma.w@outlook.com", "+447911123456", null, "UK998877A", "British", "Corporate Client");
            var g3 = Guest.Create(org.Id, "คุณกานดา ธนสมบัติ", "kanda.t@hotmail.com", "+66898765432", "3100600987654", null, "Thai", "ขอห้องชั้น 2 วิวสวย");

            dbContext.Guests.AddRange(g1, g2, g3);
            await dbContext.SaveChangesAsync(ct);

            var demoRooms = await dbContext.Rooms
                .IgnoreQueryFilters()
                .Where(r => r.PropertyId == property.Id)
                .ToListAsync(ct);

            var r101 = demoRooms.FirstOrDefault(r => r.RoomNumber == "101");
            var r102 = demoRooms.FirstOrDefault(r => r.RoomNumber == "102");
            var r201 = demoRooms.FirstOrDefault(r => r.RoomNumber == "201");

            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Res 1: In-House Guest (CheckedIn today)
            if (r101 != null)
            {
                var res1 = Reservation.Create(
                    org.Id, property.Id, g1.Id,
                    today.AddDays(-1), today.AddDays(2),
                    2, 0, r101.RoomTypeId, r101.Id,
                    1800m, 5400m, "THB", ReservationSource.Phone,
                    "ขอผ้าเช็ดตัวเพิ่ม 2 ผืน", "เข้าพักแล้ว");
                res1.Confirm();
                res1.CheckIn(DateTimeOffset.UtcNow.AddDays(-1));
                dbContext.Reservations.Add(res1);
                foreach (var h in res1.StatusHistories) dbContext.ReservationStatusHistories.Add(h);
            }

            // Res 2: Confirmed Arrival (Tomorrow)
            if (r102 != null)
            {
                var res2 = Reservation.Create(
                    org.Id, property.Id, g2.Id,
                    today.AddDays(1), today.AddDays(4),
                    1, 0, r102.RoomTypeId, r102.Id,
                    1800m, 5400m, "THB", ReservationSource.Online,
                    "Airport pick-up needed", "ลูกค้ายืนยันการชำระเงินมัดจำแล้ว");
                res2.Confirm();
                dbContext.Reservations.Add(res2);
                foreach (var h in res2.StatusHistories) dbContext.ReservationStatusHistories.Add(h);
            }

            // Res 3: Pending Walk-In
            if (r201 != null)
            {
                var res3 = Reservation.Create(
                    org.Id, property.Id, g3.Id,
                    today.AddDays(2), today.AddDays(5),
                    2, 1, r201.RoomTypeId, r201.Id,
                    3200m, 9600m, "THB", ReservationSource.WalkIn,
                    "ขอเตียงเด็ก", "รอยืนยันมัดจำภายในเที่ยงวันพรุ่งนี้");
                dbContext.Reservations.Add(res3);
                foreach (var h in res3.StatusHistories) dbContext.ReservationStatusHistories.Add(h);
            }

            await dbContext.SaveChangesAsync(ct);
        }

        // 5. Seed demo apartment property if none exists
        var aptProperty = await dbContext.Properties
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.OrganizationId == org.Id && p.Code == "MAN-01", ct);

        if (aptProperty == null)
        {
            aptProperty = Property.Create(
                organizationId: org.Id,
                name: "StayFlow Resident Mansion (อพาร์ตเมนต์)",
                code: "MAN-01",
                businessType: PropertyType.Apartment,
                ianaTimeZone: "Asia/Bangkok",
                defaultCurrency: "THB",
                address: "รัชดาภิเษก ซอย 7, ดินแดง, กรุงเทพฯ 10400",
                contactPhone: "+6629998888",
                contactEmail: "mansion@stayflow.com"
            );
            dbContext.Properties.Add(aptProperty);
            await dbContext.SaveChangesAsync(ct);

            var aptRt = RoomType.Create(org.Id, aptProperty.Id, "Studio Room", "STD-01", 5500m, 2, 2, 2, 0, "ห้องสตูดิโอ 32 ตร.ม. พร้อมเฟอร์นิเจอร์ครบ");
            dbContext.RoomTypes.Add(aptRt);
            await dbContext.SaveChangesAsync(ct);

            var r301 = Room.Create(org.Id, aptProperty.Id, aptRt.Id, "301", null, null, "Room 301 (ห้อง 301)", 2, RoomOperationalStatus.Clean, "ห้องพร้อมอยู่");
            var r302 = Room.Create(org.Id, aptProperty.Id, aptRt.Id, "302", null, null, "Room 302 (ห้อง 302)", 2, RoomOperationalStatus.Clean, "ห้องพร้อมอยู่");
            var r303 = Room.Create(org.Id, aptProperty.Id, aptRt.Id, "303", null, null, "Room 303 (ห้อง 303)", 2, RoomOperationalStatus.Dirty, "รอทำความสะอาดหลังย้ายออก");
            dbContext.Rooms.AddRange(r301, r302, r303);
            await dbContext.SaveChangesAsync(ct);

            // Tenant
            var t1 = Tenant.Create(org.Id, aptProperty.Id, "สมชาย รักสงบ", "1100200300400", "0812345678", "somchai@stayflow.com", "สมศรี รักสงบ", "0819998888");
            dbContext.Tenants.Add(t1);
            await dbContext.SaveChangesAsync(ct);

            // Agreement
            var agr1 = RentalAgreement.Create(
                org.Id, aptProperty.Id, "AGR-MAN-301", t1.Id, r301.Id,
                new DateOnly(2026, 6, 1), new DateOnly(2027, 5, 31),
                5500m, 11000m, 25, "สัญญาเช่า 1 ปี");
            agr1.RecordDepositPayment(11000m);
            agr1.Activate();
            dbContext.RentalAgreements.Add(agr1);
            await dbContext.SaveChangesAsync(ct);

            // Meter readings
            var mElec = MeterReading.Record(
                org.Id, aptProperty.Id, r301.Id, UtilityType.Electricity,
                new DateOnly(2026, 11, 25), "2026-11", 120m, 180m, 7.00m, user.Id, "จดมิเตอร์ไฟประจำเดือน");
            var mWater = MeterReading.Record(
                org.Id, aptProperty.Id, r301.Id, UtilityType.Water,
                new DateOnly(2026, 11, 25), "2026-11", 45m, 55m, 18.00m, user.Id, "จดมิเตอร์น้ำประจำเดือน");
            dbContext.MeterReadings.AddRange(mElec, mWater);
            await dbContext.SaveChangesAsync(ct);

            // Monthly Invoice
            var inv1 = MonthlyInvoice.Create(
                org.Id, aptProperty.Id, "INV-202611-301", agr1.Id, t1.Id, r301.Id,
                "2026-11", new DateOnly(2026, 12, 5));
            inv1.AddItem(InvoiceItemType.Rent, "ค่าเช่าห้องพักประจำเดือน 2026-11 (ห้อง 301)", 5500m, 5500m, 1);
            inv1.AddItem(InvoiceItemType.Electricity, "ค่าไฟฟ้า (120 - 180 = 60 หน่วย @ 7.00 บ.)", 420m, 7.00m, 60);
            inv1.AddItem(InvoiceItemType.Water, "ค่าน้ำประปา (45 - 55 = 10 หน่วย @ 18.00 บ.)", 180m, 18.00m, 10);
            inv1.Issue();
            dbContext.MonthlyInvoices.Add(inv1);
            foreach (var itm in inv1.Items) dbContext.MonthlyInvoiceItems.Add(itm);
            await dbContext.SaveChangesAsync(ct);
        }

        var permissions = Permissions.GetPermissionsForRole(membership.Role);
        var accessToken = tokenService.GenerateAccessToken(user, org.Id, membership.Role, permissions);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshToken = RefreshToken.Create(user.Id, rawRefreshToken, TimeSpan.FromDays(30));
        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(ct);

        var orgList = new List<UserOrganizationDto>
        {
            new(org.Id, org.Name, org.Slug, membership.Role.ToString())
        };

        return Results.Ok(new AuthResponse(
            accessToken,
            rawRefreshToken,
            new UserDto(user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage),
            org.Id,
            membership.Role.ToString(),
            permissions,
            orgList
        ));
    }

    private static async Task<IResult> ForgotPasswordAsync(
        [FromBody] ForgotPasswordRequest request,
        StayFlowDbContext dbContext,
        IEmailService emailService,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("ForgotPassword");

        // Always return 200 — do not reveal whether the email exists
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.Ok(new { message = "If that email is registered, a reset link has been sent." });

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Email == normalizedEmail && u.IsActive, ct);

        if (user == null)
        {
            // Don't reveal non-existence; log at debug only
            logger.LogDebug("ForgotPassword: no active user for email {Email}", normalizedEmail);
            return Results.Ok(new { message = "If that email is registered, a reset link has been sent." });
        }

        if (!emailService.IsConfigured)
        {
            logger.LogWarning(
                "ForgotPassword: SMTP is not configured — cannot send reset email to {Email}. " +
                "Set Email__SmtpHost (and related Email__ vars) in environment variables.",
                normalizedEmail);
            return Results.Ok(new { message = "If that email is registered, a reset link has been sent." });
        }

        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var resetToken = PasswordResetToken.Create(user.Id, rawToken);
        dbContext.PasswordResetTokens.Add(resetToken);
        await dbContext.SaveChangesAsync(ct);

        var frontendBase = configuration["AppSettings:FrontendBaseUrl"]?.TrimEnd('/') ?? "";
        var resetLink = $"{frontendBase}/reset-password?token={rawToken}";
        var appName = configuration["AppSettings:AppName"] ?? "StayFlow PMS";

        var emailMsg = EmailTemplates.PasswordResetEmail(user.Email, user.FullName, resetLink, appName);
        try
        {
            await emailService.SendAsync(emailMsg, ct);
            logger.LogInformation("ForgotPassword: reset email sent to {Email}", normalizedEmail);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ForgotPassword: failed to send reset email to {Email}", normalizedEmail);
            // Token already saved — user can retry; do not surface exception to client
        }

        return Results.Ok(new { message = "If that email is registered, a reset link has been sent." });
    }

    private static async Task<IResult> ResetPasswordAsync(
        [FromBody] ResetPasswordRequest request,
        StayFlowDbContext dbContext,
        IPasswordHasher passwordHasher,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Results.Problem("Reset token is required.", statusCode: StatusCodes.Status400BadRequest);

        if (string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < 8 ||
            !request.NewPassword.Any(char.IsLetter) ||
            !request.NewPassword.Any(char.IsDigit))
            return Results.Problem("Password must be at least 8 characters and contain at least one letter and one digit.", statusCode: StatusCodes.Status400BadRequest);

        var tokenRecord = await dbContext.PasswordResetTokens
            .FirstOrDefaultAsync(t => t.Token == request.Token, ct);

        if (tokenRecord == null || !tokenRecord.IsValid)
            return Results.Problem("This reset link is invalid or has expired. Please request a new one.", statusCode: StatusCodes.Status400BadRequest);

        var user = await dbContext.Users.FindAsync([tokenRecord.UserId], ct);
        if (user == null || !user.IsActive)
            return Results.Problem("User not found or inactive.", statusCode: StatusCodes.Status400BadRequest);

        user.ChangePassword(passwordHasher.HashPassword(request.NewPassword));
        tokenRecord.MarkUsed();
        await dbContext.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        StayFlowDbContext dbContext,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (!tenantContext.UserId.HasValue)
        {
            return Results.Unauthorized();
        }

        var user = await dbContext.Users.FindAsync([tenantContext.UserId.Value], ct);
        if (user == null)
        {
            return Results.Unauthorized();
        }

        var memberships = await dbContext.OrganizationMemberships
            .Where(m => m.UserId == user.Id)
            .Join(dbContext.Organizations.Where(o => o.IsActive),
                m => m.OrganizationId,
                o => o.Id,
                (m, o) => new { Membership = m, Organization = o })
            .ToListAsync(ct);

        var activeOrgId = tenantContext.OrganizationId;
        var activeItem = memberships.FirstOrDefault(m => m.Organization.Id == activeOrgId) ?? memberships.FirstOrDefault();

        var orgDtos = memberships.Select(x => new UserOrganizationDto(
            x.Organization.Id,
            x.Organization.Name,
            x.Organization.Slug,
            x.Membership.Role.ToString()
        )).ToList();

        return Results.Ok(new
        {
            user = new UserDto(user.Id, user.Email, user.FullName, user.PhoneNumber, user.PreferredLanguage),
            activeOrganizationId = activeItem?.Organization.Id,
            activeRole = activeItem?.Membership.Role.ToString(),
            permissions = tenantContext.Permissions,
            organizations = orgDtos
        });
    }
}

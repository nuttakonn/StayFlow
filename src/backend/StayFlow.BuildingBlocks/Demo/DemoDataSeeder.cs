using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.Domain.Identity;
using StayFlow.BuildingBlocks.Persistence;
using StayFlow.BuildingBlocks.Security;

namespace StayFlow.BuildingBlocks.Demo;

/// <summary>
/// Seeds a fixed demo account and hotel property into the InMemory DB on every startup.
/// Only runs when the DB is empty. Remove this class before using a real database.
/// Demo credentials: admin@demo.com / Demo1234!
/// </summary>
public static class DemoDataSeeder
{
    public const string DemoEmail = "admin@demo.com";
    public const string DemoPassword = "Demo1234!";
    public const string DemoOrgName = "Demo Hotel Group";
    public const string DemoPropertyName = "Demo Grand Hotel";

    public static async Task EnsureDemoDataAsync(StayFlowDbContext db, IPasswordHasher passwordHasher)
    {
        // Only seed if no users exist (InMemory DB just started)
        if (await db.Users.AnyAsync())
            return;

        // ── 1. User ────────────────────────────────────────────────────────
        var passwordHash = passwordHasher.HashPassword(DemoPassword);
        var user = User.Create(DemoEmail, passwordHash, "Demo Admin", "080-000-0000");
        db.Users.Add(user);

        // ── 2. Organization ────────────────────────────────────────────────
        var org = Organization.Create(DemoOrgName, "demo-hotel-group");
        db.Organizations.Add(org);

        var membership = OrganizationMembership.Create(org.Id, user.Id, OrganizationRole.Owner);
        db.OrganizationMemberships.Add(membership);

        // ── 3. Property ────────────────────────────────────────────────────
        var property = Property.Create(
            organizationId: org.Id,
            name: DemoPropertyName,
            code: "DGH",
            businessType: PropertyType.Hotel,
            ianaTimeZone: "Asia/Bangkok",
            defaultCurrency: "THB",
            address: "123 ถนนสุขุมวิท กรุงเทพฯ",
            contactPhone: "02-000-0000",
            contactEmail: "hotel@demo.com");
        db.Properties.Add(property);

        // ── 4. Room types ──────────────────────────────────────────────────
        var standardType = RoomType.Create(
            organizationId: org.Id,
            propertyId: property.Id,
            name: "Standard",
            code: "STD",
            baseRate: 1500m,
            standardOccupancy: 2,
            maxOccupancy: 2);
        db.RoomTypes.Add(standardType);

        var deluxeType = RoomType.Create(
            organizationId: org.Id,
            propertyId: property.Id,
            name: "Deluxe",
            code: "DLX",
            baseRate: 2500m,
            standardOccupancy: 2,
            maxOccupancy: 3);
        db.RoomTypes.Add(deluxeType);

        var suiteType = RoomType.Create(
            organizationId: org.Id,
            propertyId: property.Id,
            name: "Suite",
            code: "STE",
            baseRate: 4500m,
            standardOccupancy: 2,
            maxOccupancy: 4);
        db.RoomTypes.Add(suiteType);

        // ── 5. Rooms ───────────────────────────────────────────────────────
        var rooms = new[]
        {
            Room.Create(org.Id, property.Id, standardType.Id, "101", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(org.Id, property.Id, standardType.Id, "102", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(org.Id, property.Id, standardType.Id, "103", capacity: 2, operationalStatus: RoomOperationalStatus.Inspected),
            Room.Create(org.Id, property.Id, standardType.Id, "104", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(org.Id, property.Id, deluxeType.Id,   "201", capacity: 2, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(org.Id, property.Id, deluxeType.Id,   "202", capacity: 2, operationalStatus: RoomOperationalStatus.Inspected),
            Room.Create(org.Id, property.Id, deluxeType.Id,   "203", capacity: 3, operationalStatus: RoomOperationalStatus.Clean),
            Room.Create(org.Id, property.Id, suiteType.Id,    "301", capacity: 4, operationalStatus: RoomOperationalStatus.Inspected),
        };
        db.Rooms.AddRange(rooms);

        await db.SaveChangesAsync();
    }
}

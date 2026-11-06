using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Domain.Subscriptions;
using StayFlow.BuildingBlocks.Persistence;

namespace StayFlow.BuildingBlocks.Entitlements;

/// <summary>
/// Idempotently ensures the default plan catalog exists. Runs explicitly at startup
/// (Program.cs) rather than via EF Core model seed data (HasData): this codebase has
/// no EF Core migrations yet (schema provisioning for PostgreSQL is a pre-existing gap
/// tracked in docs/adr/0008), so HasData would silently do nothing outside the
/// InMemory test provider. An explicit, idempotent seed step works for both and fails
/// loudly if the schema genuinely isn't there, instead of pretending it is.
/// </summary>
public static class SubscriptionPlanSeeder
{
    public static async Task EnsureDefaultPlansAsync(StayFlowDbContext dbContext, CancellationToken ct = default)
    {
        var existingKeys = await dbContext.SubscriptionPlans
            .IgnoreQueryFilters()
            .Select(p => p.Key)
            .ToListAsync(ct);

        if (!existingKeys.Contains("starter"))
        {
            dbContext.SubscriptionPlans.Add(SubscriptionPlan.Create(
                key: "starter",
                name: "Starter",
                maxUsers: 5,
                maxProperties: 1,
                trialDays: 14,
                capabilityKeys:
                [
                    EntitlementKeys.HotelReservations,
                    EntitlementKeys.ApartmentRentals,
                    EntitlementKeys.FinanceInvoices,
                    EntitlementKeys.BrandingOrganization
                ]));
        }

        if (!existingKeys.Contains("professional"))
        {
            dbContext.SubscriptionPlans.Add(SubscriptionPlan.Create(
                key: "professional",
                name: "Professional",
                maxUsers: 20,
                maxProperties: 5,
                trialDays: 14,
                capabilityKeys:
                [
                    EntitlementKeys.HotelReservations,
                    EntitlementKeys.HotelHousekeeping,
                    EntitlementKeys.ApartmentRentals,
                    EntitlementKeys.ApartmentUtilities,
                    EntitlementKeys.FinanceInvoices,
                    EntitlementKeys.ReportingAdvanced,
                    EntitlementKeys.BrandingOrganization,
                    EntitlementKeys.BrandingProperty
                ]));
        }

        if (!existingKeys.Contains("enterprise"))
        {
            dbContext.SubscriptionPlans.Add(SubscriptionPlan.Create(
                key: "enterprise",
                name: "Enterprise",
                maxUsers: null,
                maxProperties: null,
                trialDays: 14,
                capabilityKeys: EntitlementKeys.All));
        }

        await dbContext.SaveChangesAsync(ct);
    }
}

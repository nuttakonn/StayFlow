using StayFlow.BuildingBlocks.Domain.Tenancy;

namespace StayFlow.BuildingBlocks.Domain.Authorization;

public static class Permissions
{
    // Organizations
    public const string OrganizationsView = "organizations:view";
    public const string OrganizationsManage = "organizations:manage";

    // Properties
    public const string PropertiesView = "properties:view";
    public const string PropertiesManage = "properties:manage";

    // Rooms & Inventory
    public const string RoomsView = "rooms:view";
    public const string RoomsManage = "rooms:manage";

    // Reservations
    public const string ReservationsView = "reservations:view";
    public const string ReservationsCreate = "reservations:create";
    public const string ReservationsManage = "reservations:manage";

    // Stay Operations
    public const string StaysCheckIn = "stays:checkin";
    public const string StaysCheckOut = "stays:checkout";

    // Billing & Folios
    public const string FoliosView = "folios:view";
    public const string FoliosManage = "folios:manage";
    public const string PaymentsRecord = "payments:record";

    // Apartments (Phase 6)
    public const string AgreementsView = "agreements:view";
    public const string AgreementsManage = "agreements:manage";
    public const string MetersRecord = "meters:record";
    public const string InvoicesManage = "invoices:manage";

    // Branding
    public const string BrandingView = "branding:view";
    public const string BrandingManage = "branding:manage";

    // Audit
    public const string AuditView = "audit:view";

    // Subscriptions & Entitlements (Phase 7)
    public const string SubscriptionsView = "subscriptions:view";
    public const string SubscriptionsManage = "subscriptions:manage";

    public static IReadOnlyCollection<string> GetPermissionsForRole(OrganizationRole role)
    {
        return role switch
        {
            OrganizationRole.Owner =>
            [
                OrganizationsView, OrganizationsManage,
                PropertiesView, PropertiesManage,
                RoomsView, RoomsManage,
                ReservationsView, ReservationsCreate, ReservationsManage,
                StaysCheckIn, StaysCheckOut,
                FoliosView, FoliosManage, PaymentsRecord,
                AgreementsView, AgreementsManage, MetersRecord, InvoicesManage,
                BrandingView, BrandingManage,
                AuditView,
                SubscriptionsView, SubscriptionsManage
            ],
            OrganizationRole.Admin =>
            [
                OrganizationsView,
                PropertiesView, PropertiesManage,
                RoomsView, RoomsManage,
                ReservationsView, ReservationsCreate, ReservationsManage,
                StaysCheckIn, StaysCheckOut,
                FoliosView, FoliosManage, PaymentsRecord,
                AgreementsView, AgreementsManage, MetersRecord, InvoicesManage,
                BrandingView, BrandingManage,
                AuditView,
                SubscriptionsView
            ],
            OrganizationRole.Manager =>
            [
                OrganizationsView,
                PropertiesView,
                RoomsView, RoomsManage,
                ReservationsView, ReservationsCreate, ReservationsManage,
                StaysCheckIn, StaysCheckOut,
                FoliosView, FoliosManage, PaymentsRecord,
                AgreementsView, AgreementsManage, MetersRecord, InvoicesManage,
                BrandingView
            ],
            OrganizationRole.Staff =>
            [
                OrganizationsView,
                PropertiesView,
                RoomsView,
                ReservationsView, ReservationsCreate,
                StaysCheckIn, StaysCheckOut,
                FoliosView, PaymentsRecord,
                AgreementsView, MetersRecord, InvoicesManage,
                BrandingView
            ],
            OrganizationRole.Viewer =>
            [
                OrganizationsView,
                PropertiesView,
                RoomsView,
                ReservationsView,
                FoliosView,
                AgreementsView,
                BrandingView
            ],
            _ => []
        };
    }
}

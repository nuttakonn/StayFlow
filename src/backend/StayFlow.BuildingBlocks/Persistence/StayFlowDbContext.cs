using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StayFlow.BuildingBlocks.Common;
using StayFlow.BuildingBlocks.Domain.Audit;
using StayFlow.BuildingBlocks.Domain.ChannelManager;
using StayFlow.BuildingBlocks.Domain.Apartments;
using StayFlow.BuildingBlocks.Domain.Rates;
using StayFlow.BuildingBlocks.Domain.Services;
using StayFlow.BuildingBlocks.Domain.Notifications;
using StayFlow.BuildingBlocks.Domain.ThaiCompliance;
using StayFlow.BuildingBlocks.Domain.Billing;
using StayFlow.BuildingBlocks.Domain.Maintenance;
using StayFlow.BuildingBlocks.Domain.Branding;
using StayFlow.BuildingBlocks.Domain.Identity;
using StayFlow.BuildingBlocks.Domain.Inventory;
using StayFlow.BuildingBlocks.Domain.Parties;
using StayFlow.BuildingBlocks.Domain.Properties;
using StayFlow.BuildingBlocks.Domain.Reservations;
using StayFlow.BuildingBlocks.Domain.Subscriptions;
using StayFlow.BuildingBlocks.Domain.Tenancy;
using StayFlow.BuildingBlocks.MultiTenancy;

namespace StayFlow.BuildingBlocks.Persistence;

public class StayFlowDbContext : DbContext
{
    private readonly ITenantContext? _tenantContext;

    public StayFlowDbContext(
        DbContextOptions<StayFlowDbContext> options,
        ITenantContext? tenantContext = null)
        : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<PropertyMembership> PropertyMemberships => Set<PropertyMembership>();
    public DbSet<Property> Properties => Set<Property>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Floor> Floors => Set<Floor>();
    public DbSet<RoomType> RoomTypes => Set<RoomType>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoomStatusHistory> RoomStatusHistories => Set<RoomStatusHistory>();
    public DbSet<BrandingSettings> BrandingSettings => Set<BrandingSettings>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    // Phase 3: Guests & Reservations
    public DbSet<Guest> Guests => Set<Guest>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<ReservationStatusHistory> ReservationStatusHistories => Set<ReservationStatusHistory>();

    // Phase 5: Billing & Payments
    public DbSet<Folio> Folios => Set<Folio>();
    public DbSet<FolioItem> FolioItems => Set<FolioItem>();
    public DbSet<Payment> Payments => Set<Payment>();

    // Phase 6: Apartments & Monthly Rentals
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<RentalAgreement> RentalAgreements => Set<RentalAgreement>();
    public DbSet<MeterReading> MeterReadings => Set<MeterReading>();
    public DbSet<MonthlyInvoice> MonthlyInvoices => Set<MonthlyInvoice>();
    public DbSet<MonthlyInvoiceItem> MonthlyInvoiceItems => Set<MonthlyInvoiceItem>();
    public DbSet<InvoicePaymentRecord> InvoicePaymentRecords => Set<InvoicePaymentRecord>();

    // Phase 7: Subscriptions & Entitlements
    public DbSet<MaintenanceRequest> MaintenanceRequests => Set<MaintenanceRequest>();

    // M9: Channel Manager
    public DbSet<ChannelConnection> ChannelConnections => Set<ChannelConnection>();
    public DbSet<ChannelRateMapping> ChannelRateMappings => Set<ChannelRateMapping>();
    public DbSet<ChannelReservation> ChannelReservations => Set<ChannelReservation>();

    // M10: Rate Plans, Coupons, Group Bookings
    public DbSet<RatePlan> RatePlans => Set<RatePlan>();
    public DbSet<RateRule> RateRules => Set<RateRule>();
    public DbSet<RatePlanRestriction> RatePlanRestrictions => Set<RatePlanRestriction>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<GroupBooking> GroupBookings => Set<GroupBooking>();

    // M10.5: Ancillary Services, Packages, Quotations
    public DbSet<ServiceItem> ServiceItems => Set<ServiceItem>();
    public DbSet<Package> Packages => Set<Package>();
    public DbSet<PackageItem> PackageItems => Set<PackageItem>();
    public DbSet<Quotation> Quotations => Set<Quotation>();

    // M11: Thai Compliance
    public DbSet<ThaiComplianceRecord> ThaiComplianceRecords => Set<ThaiComplianceRecord>();
    public DbSet<ThaiComplianceAmendment> ThaiComplianceAmendments => Set<ThaiComplianceAmendment>();

    // M12: Email Notification Settings
    public DbSet<EmailNotificationSettings> EmailNotificationSettings => Set<EmailNotificationSettings>();

    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<PlanEntitlement> PlanEntitlements => Set<PlanEntitlement>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionHistory> SubscriptionHistories => Set<SubscriptionHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User Entity
        modelBuilder.Entity<User>(b =>
        {
            b.ToTable("users");
            b.HasKey(u => u.Id);
            b.Property(u => u.Email).HasMaxLength(255).IsRequired();
            b.HasIndex(u => u.Email).IsUnique();
            b.Property(u => u.FullName).HasMaxLength(255).IsRequired();
            b.Property(u => u.PhoneNumber).HasMaxLength(50);
            b.Property(u => u.PreferredLanguage)
                .HasMaxLength(10)
                .IsRequired()
                .HasDefaultValue(SupportedLanguages.Default);
        });

        // RefreshToken
        modelBuilder.Entity<RefreshToken>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(r => r.Id);
            b.Property(r => r.Token).HasMaxLength(500).IsRequired();
            b.HasIndex(r => r.Token).IsUnique();
            b.HasIndex(r => r.UserId);
        });

        // Organization
        modelBuilder.Entity<Organization>(b =>
        {
            b.ToTable("organizations");
            b.HasKey(o => o.Id);
            b.Property(o => o.Name).HasMaxLength(255).IsRequired();
            b.Property(o => o.Slug).HasMaxLength(255).IsRequired();
            b.HasIndex(o => o.Slug).IsUnique();
        });

        // OrganizationMembership
        modelBuilder.Entity<OrganizationMembership>(b =>
        {
            b.ToTable("organization_memberships");
            b.HasKey(m => m.Id);
            b.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();
            b.HasIndex(m => m.UserId);
        });

        // PropertyMembership — grants a member access to a specific property
        modelBuilder.Entity<PropertyMembership>(b =>
        {
            b.ToTable("property_memberships");
            b.HasKey(pm => pm.Id);
            // One membership row per (member, property)
            b.HasIndex(pm => new { pm.OrganizationMembershipId, pm.PropertyId }).IsUnique();
            b.HasIndex(pm => pm.OrganizationId);
            b.HasIndex(pm => pm.PropertyId);
        });

        // Property
        modelBuilder.Entity<Property>(b =>
        {
            b.ToTable("properties");
            b.HasKey(p => p.Id);
            b.Property(p => p.Name).HasMaxLength(255).IsRequired();
            b.Property(p => p.Code).HasMaxLength(50).IsRequired();
            b.Property(p => p.IanaTimeZone).HasMaxLength(100).IsRequired();
            b.Property(p => p.DefaultCurrency).HasMaxLength(3).IsRequired();
            b.HasIndex(p => new { p.OrganizationId, p.Code }).IsUnique();
            b.HasIndex(p => p.OrganizationId);

            // Tenant Query Filter
            b.HasQueryFilter(p => !_tenantContext!.OrganizationId.HasValue || p.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Building
        modelBuilder.Entity<Building>(b =>
        {
            b.ToTable("buildings");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(255).IsRequired();
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Floor
        modelBuilder.Entity<Floor>(b =>
        {
            b.ToTable("floors");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.PropertyId, x.BuildingId, x.FloorNumber }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // RoomType
        modelBuilder.Entity<RoomType>(b =>
        {
            b.ToTable("room_types");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(255).IsRequired();
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.BaseRate).HasPrecision(18, 2);
            b.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Room
        modelBuilder.Entity<Room>(b =>
        {
            b.ToTable("rooms");
            b.HasKey(x => x.Id);
            b.Property(x => x.RoomNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.DisplayName).HasMaxLength(255);
            b.HasIndex(x => new { x.PropertyId, x.RoomNumber }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.RoomTypeId);
            b.HasIndex(x => x.OperationalStatus);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // RoomStatusHistory
        modelBuilder.Entity<RoomStatusHistory>(b =>
        {
            b.ToTable("room_status_histories");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.RoomId);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.CreatedAtUtc);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // BrandingSettings
        modelBuilder.Entity<BrandingSettings>(b =>
        {
            b.ToTable("branding_settings");
            b.HasKey(s => s.Id);
            b.Property(s => s.DisplayName).HasMaxLength(255).IsRequired();
            b.Property(s => s.PrimaryColor).HasMaxLength(20).IsRequired();
            b.Property(s => s.SecondaryColor).HasMaxLength(20).IsRequired();
            b.HasIndex(s => new { s.OrganizationId, s.PropertyId }).IsUnique();

            // Tenant Query Filter
            b.HasQueryFilter(s => !_tenantContext!.OrganizationId.HasValue || s.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // AuditEvent
        modelBuilder.Entity<AuditEvent>(b =>
        {
            b.ToTable("audit_events");
            b.HasKey(a => a.Id);
            b.Property(a => a.EntityName).HasMaxLength(100).IsRequired();
            b.Property(a => a.Action).HasMaxLength(50).IsRequired();
            b.HasIndex(a => a.OrganizationId);
            b.HasIndex(a => a.TimestampUtc);

            // Tenant Query Filter
            b.HasQueryFilter(a => !_tenantContext!.OrganizationId.HasValue || a.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Guest (Phase 3)
        modelBuilder.Entity<Guest>(b =>
        {
            b.ToTable("guests");
            b.HasKey(g => g.Id);
            b.Property(g => g.FullName).HasMaxLength(255).IsRequired();
            b.Property(g => g.Email).HasMaxLength(255);
            b.Property(g => g.Phone).HasMaxLength(50);
            b.Property(g => g.NationalId).HasMaxLength(50);
            b.Property(g => g.PassportNumber).HasMaxLength(50);
            b.Property(g => g.Nationality).HasMaxLength(100);
            b.HasIndex(g => g.OrganizationId);
            b.HasIndex(g => new { g.OrganizationId, g.Email });
            b.HasQueryFilter(g => !_tenantContext!.OrganizationId.HasValue || g.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Reservation (Phase 3)
        modelBuilder.Entity<Reservation>(b =>
        {
            b.ToTable("reservations");
            b.HasKey(r => r.Id);
            b.Property(r => r.QuotedRate).HasPrecision(18, 2);
            b.Property(r => r.TotalAmount).HasPrecision(18, 2);
            b.Property(r => r.Currency).HasMaxLength(3).IsRequired();
            b.Property(r => r.SpecialRequests).HasMaxLength(1000);
            b.Property(r => r.Notes).HasMaxLength(1000);
            b.Property(r => r.CancellationReason).HasMaxLength(500);
            b.Property(r => r.CouponCode).HasMaxLength(100);
            b.Property(r => r.DiscountAmount).HasPrecision(18, 4);
            // Optimistic concurrency token
            b.Property(r => r.RowVersion).IsConcurrencyToken();
            b.HasIndex(r => r.OrganizationId);
            b.HasIndex(r => r.PropertyId);
            b.HasIndex(r => r.GuestId);
            b.HasIndex(r => r.RoomId);
            b.HasIndex(r => r.Status);
            b.HasIndex(r => r.CheckInDate);
            b.HasIndex(r => r.CheckOutDate);
            b.HasIndex(r => r.GroupBookingId);
            b.HasQueryFilter(r => !_tenantContext!.OrganizationId.HasValue || r.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ReservationStatusHistory (Phase 3)
        modelBuilder.Entity<ReservationStatusHistory>(b =>
        {
            b.ToTable("reservation_status_histories");
            b.HasKey(h => h.Id);
            b.Property(h => h.Note).HasMaxLength(500);
            b.HasIndex(h => h.ReservationId);
            b.HasIndex(h => h.OrganizationId);
            b.HasIndex(h => h.CreatedAtUtc);
            b.HasQueryFilter(h => !_tenantContext!.OrganizationId.HasValue || h.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Folio (Phase 5)
        modelBuilder.Entity<Folio>(b =>
        {
            b.ToTable("folios");
            b.HasKey(f => f.Id);
            b.Property(f => f.FolioNumber).HasMaxLength(50).IsRequired();
            b.Property(f => f.Currency).HasMaxLength(3).IsRequired();
            b.Property(f => f.Notes).HasMaxLength(1000);
            b.HasIndex(f => f.OrganizationId);
            b.HasIndex(f => f.PropertyId);
            b.HasIndex(f => f.ReservationId);
            b.HasIndex(f => new { f.PropertyId, f.FolioNumber }).IsUnique();
            b.HasQueryFilter(f => !_tenantContext!.OrganizationId.HasValue || f.OrganizationId == _tenantContext.OrganizationId.Value);

            b.HasMany(f => f.Items)
                .WithOne()
                .HasForeignKey(i => i.FolioId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(f => f.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(f => f.Payments)
                .WithOne()
                .HasForeignKey(p => p.FolioId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Navigation(f => f.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // FolioItem (Phase 5)
        modelBuilder.Entity<FolioItem>(b =>
        {
            b.ToTable("folio_items");
            b.HasKey(i => i.Id);
            b.Property(i => i.Description).HasMaxLength(255).IsRequired();
            b.Property(i => i.UnitPrice).HasPrecision(18, 2);
            b.Property(i => i.Amount).HasPrecision(18, 2);
            b.HasIndex(i => i.OrganizationId);
            b.HasIndex(i => i.PropertyId);
            b.HasIndex(i => i.FolioId);
            b.HasQueryFilter(i => !_tenantContext!.OrganizationId.HasValue || i.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Payment (Phase 5)
        modelBuilder.Entity<Payment>(b =>
        {
            b.ToTable("payments");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.Currency).HasMaxLength(3).IsRequired();
            b.Property(p => p.ReferenceNumber).HasMaxLength(100);
            b.Property(p => p.Notes).HasMaxLength(500);
            b.Property(p => p.ReversalReason).HasMaxLength(500);
            b.HasIndex(p => p.OrganizationId);
            b.HasIndex(p => p.PropertyId);
            b.HasIndex(p => p.FolioId);
            b.HasIndex(p => p.CreatedAtUtc);
            b.HasQueryFilter(p => !_tenantContext!.OrganizationId.HasValue || p.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // Tenant (Phase 6)
        modelBuilder.Entity<Tenant>(b =>
        {
            b.ToTable("tenants");
            b.HasKey(t => t.Id);
            b.Property(t => t.FullName).HasMaxLength(150).IsRequired();
            b.Property(t => t.NationalIdOrPassport).HasMaxLength(50);
            b.Property(t => t.PhoneNumber).HasMaxLength(30).IsRequired();
            b.Property(t => t.Email).HasMaxLength(150);
            b.Property(t => t.EmergencyContactName).HasMaxLength(150);
            b.Property(t => t.EmergencyContactPhone).HasMaxLength(30);
            b.HasIndex(t => t.OrganizationId);
            b.HasIndex(t => t.PropertyId);
            b.HasIndex(t => t.PhoneNumber);
            b.HasQueryFilter(t => !_tenantContext!.OrganizationId.HasValue || t.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // RentalAgreement (Phase 6)
        modelBuilder.Entity<RentalAgreement>(b =>
        {
            b.ToTable("rental_agreements");
            b.HasKey(a => a.Id);
            b.Property(a => a.AgreementNumber).HasMaxLength(50).IsRequired();
            b.Property(a => a.MonthlyRent).HasPrecision(18, 2);
            b.Property(a => a.SecurityDeposit).HasPrecision(18, 2);
            b.Property(a => a.DepositPaid).HasPrecision(18, 2);
            b.Property(a => a.DepositRefundedAmount).HasPrecision(18, 2);
            b.Property(a => a.DepositDeductedAmount).HasPrecision(18, 2);
            b.Property(a => a.Notes).HasMaxLength(500);
            b.Property(a => a.TerminationReason).HasMaxLength(500);

            b.HasOne(a => a.Tenant)
                .WithMany()
                .HasForeignKey(a => a.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(a => a.Room)
                .WithMany()
                .HasForeignKey(a => a.RoomId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(a => a.OrganizationId);
            b.HasIndex(a => a.PropertyId);
            b.HasIndex(a => a.TenantId);
            b.HasIndex(a => a.RoomId);
            b.HasIndex(a => a.Status);
            b.HasIndex(a => new { a.PropertyId, a.AgreementNumber }).IsUnique();
            b.HasQueryFilter(a => !_tenantContext!.OrganizationId.HasValue || a.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // MeterReading (Phase 6)
        modelBuilder.Entity<MeterReading>(b =>
        {
            b.ToTable("meter_readings");
            b.HasKey(m => m.Id);
            b.Property(m => m.BillingMonth).HasMaxLength(10).IsRequired();
            b.Property(m => m.PreviousReading).HasPrecision(18, 2);
            b.Property(m => m.CurrentReading).HasPrecision(18, 2);
            b.Property(m => m.UnitPrice).HasPrecision(18, 2);
            b.Property(m => m.Notes).HasMaxLength(300);

            b.HasOne(m => m.Room)
                .WithMany()
                .HasForeignKey(m => m.RoomId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasIndex(m => m.OrganizationId);
            b.HasIndex(m => m.PropertyId);
            b.HasIndex(m => m.RoomId);
            b.HasIndex(m => new { m.PropertyId, m.RoomId, m.UtilityType, m.BillingMonth }).IsUnique();
            b.HasQueryFilter(m => !_tenantContext!.OrganizationId.HasValue || m.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // MonthlyInvoice (Phase 6)
        modelBuilder.Entity<MonthlyInvoice>(b =>
        {
            b.ToTable("monthly_invoices");
            b.HasKey(i => i.Id);
            b.Property(i => i.InvoiceNumber).HasMaxLength(50).IsRequired();
            b.Property(i => i.BillingMonth).HasMaxLength(10).IsRequired();
            b.Property(i => i.TotalAmount).HasPrecision(18, 2);
            b.Property(i => i.PaidAmount).HasPrecision(18, 2);

            b.HasOne(i => i.Agreement)
                .WithMany()
                .HasForeignKey(i => i.AgreementId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasOne(i => i.Tenant)
                .WithMany()
                .HasForeignKey(i => i.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            b.HasMany(i => i.Items)
                .WithOne()
                .HasForeignKey(item => item.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(i => i.Items).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasMany(i => i.Payments)
                .WithOne()
                .HasForeignKey(p => p.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            b.Navigation(i => i.Payments).UsePropertyAccessMode(PropertyAccessMode.Field);

            b.HasIndex(i => i.OrganizationId);
            b.HasIndex(i => i.PropertyId);
            b.HasIndex(i => i.AgreementId);
            b.HasIndex(i => i.TenantId);
            b.HasIndex(i => i.RoomId);
            b.HasIndex(i => new { i.PropertyId, i.AgreementId, i.BillingMonth }).IsUnique();
            b.HasQueryFilter(i => !_tenantContext!.OrganizationId.HasValue || i.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // MonthlyInvoiceItem (Phase 6)
        modelBuilder.Entity<MonthlyInvoiceItem>(b =>
        {
            b.ToTable("monthly_invoice_items");
            b.HasKey(item => item.Id);
            b.Property(item => item.Description).HasMaxLength(255).IsRequired();
            b.Property(item => item.Quantity).HasPrecision(18, 2);
            b.Property(item => item.UnitPrice).HasPrecision(18, 2);
            b.Property(item => item.Amount).HasPrecision(18, 2);
            b.HasIndex(item => item.OrganizationId);
            b.HasIndex(item => item.PropertyId);
            b.HasIndex(item => item.InvoiceId);
            b.HasQueryFilter(item => !_tenantContext!.OrganizationId.HasValue || item.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // InvoicePaymentRecord (Phase 6)
        modelBuilder.Entity<InvoicePaymentRecord>(b =>
        {
            b.ToTable("invoice_payment_records");
            b.HasKey(p => p.Id);
            b.Property(p => p.Amount).HasPrecision(18, 2);
            b.Property(p => p.ReferenceNumber).HasMaxLength(100);
            b.Property(p => p.Notes).HasMaxLength(500);
            b.HasIndex(p => p.OrganizationId);
            b.HasIndex(p => p.PropertyId);
            b.HasIndex(p => p.InvoiceId);
            b.HasQueryFilter(p => !_tenantContext!.OrganizationId.HasValue || p.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // MaintenanceRequest
        modelBuilder.Entity<MaintenanceRequest>(b =>
        {
            b.ToTable("maintenance_requests");
            b.HasKey(x => x.Id);
            b.Property(x => x.RoomNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.Title).HasMaxLength(255).IsRequired();
            b.Property(x => x.Description).HasMaxLength(1000);
            b.Property(x => x.ReportedByEmail).HasMaxLength(255);
            b.Property(x => x.AssignedToEmail).HasMaxLength(255);
            b.Property(x => x.ResolutionNotes).HasMaxLength(1000);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasIndex(x => x.RoomId);
            b.HasIndex(x => x.Status);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // SubscriptionPlan (Phase 7) - platform catalog, NOT tenant-scoped: no query filter.
        modelBuilder.Entity<SubscriptionPlan>(b =>
        {
            b.ToTable("subscription_plans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Key).HasMaxLength(50).IsRequired();
            b.Property(x => x.Name).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.Key).IsUnique();

            b.HasMany(x => x.Entitlements)
                .WithOne()
                .HasForeignKey(e => e.SubscriptionPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            b.Navigation(x => x.Entitlements).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        // PlanEntitlement (Phase 7)
        modelBuilder.Entity<PlanEntitlement>(b =>
        {
            b.ToTable("plan_entitlements");
            b.HasKey(x => x.Id);
            b.Property(x => x.CapabilityKey).HasMaxLength(100).IsRequired();
            b.HasIndex(x => new { x.SubscriptionPlanId, x.CapabilityKey }).IsUnique();
        });

        // Subscription (Phase 7) - tenant-scoped; only populated for DeploymentMode.SaaS orgs.
        modelBuilder.Entity<Subscription>(b =>
        {
            b.ToTable("subscriptions");
            b.HasKey(x => x.Id);
            b.Property(x => x.CancellationReason).HasMaxLength(500);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.SubscriptionPlanId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // SubscriptionHistory (Phase 7)
        modelBuilder.Entity<SubscriptionHistory>(b =>
        {
            b.ToTable("subscription_histories");
            b.HasKey(x => x.Id);
            b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            b.Property(x => x.FromValue).HasMaxLength(200);
            b.Property(x => x.ToValue).HasMaxLength(200);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.SubscriptionId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ChannelConnection (M9)
        modelBuilder.Entity<ChannelConnection>(b =>
        {
            b.ToTable("channel_connections");
            b.HasKey(x => x.Id);
            b.Property(x => x.ProviderPropertyId).HasMaxLength(100).IsRequired();
            b.Property(x => x.EncryptedApiKey).HasMaxLength(2000).IsRequired();
            b.Property(x => x.EncryptedApiSecret).HasMaxLength(2000);
            b.Property(x => x.LastSyncStatus).HasMaxLength(100);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasIndex(x => new { x.PropertyId, x.Provider }).IsUnique();
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ChannelRateMapping (M9)
        modelBuilder.Entity<ChannelRateMapping>(b =>
        {
            b.ToTable("channel_rate_mappings");
            b.HasKey(x => x.Id);
            b.Property(x => x.ChannelRoomTypeCode).HasMaxLength(100).IsRequired();
            b.Property(x => x.ChannelRatePlanCode).HasMaxLength(100).IsRequired();
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.ChannelConnectionId);
            b.HasIndex(x => new { x.ChannelConnectionId, x.RoomTypeId }).IsUnique();
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ChannelReservation (M9)
        modelBuilder.Entity<ChannelReservation>(b =>
        {
            b.ToTable("channel_reservations");
            b.HasKey(x => x.Id);
            b.Property(x => x.ChannelBookingReference).HasMaxLength(100).IsRequired();
            b.Property(x => x.RawPayload).IsRequired();
            b.Property(x => x.FailureReason).HasMaxLength(1000);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.ChannelConnectionId);
            b.HasIndex(x => new { x.ChannelConnectionId, x.ChannelBookingReference }).IsUnique();
            b.HasIndex(x => x.ProcessingStatus);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ── M10: Rate Plans ───────────────────────────────────────────────────

        modelBuilder.Entity<RatePlan>(b =>
        {
            b.ToTable("rate_plans");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Code).HasMaxLength(50).IsRequired();
            b.Property(x => x.BaseRate).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasMany(x => x.Rules).WithOne().HasForeignKey(r => r.RatePlanId);
            b.HasOne(x => x.Restriction).WithOne().HasForeignKey<RatePlanRestriction>(r => r.RatePlanId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        modelBuilder.Entity<RateRule>(b =>
        {
            b.ToTable("rate_rules");
            b.HasKey(x => x.Id);
            b.Property(x => x.RateMultiplier).HasPrecision(8, 4);
            b.Property(x => x.FlatAdjustment).HasPrecision(18, 4);
        });

        modelBuilder.Entity<RatePlanRestriction>(b =>
        {
            b.ToTable("rate_plan_restrictions");
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.RatePlanId).IsUnique();
            b.Property(x => x.BlackoutDatesJson).HasMaxLength(2000);
        });

        // ── M10: Coupons ──────────────────────────────────────────────────────

        modelBuilder.Entity<Coupon>(b =>
        {
            b.ToTable("coupons");
            b.HasKey(x => x.Id);
            b.Property(x => x.Code).HasMaxLength(100).IsRequired();
            b.Property(x => x.Value).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.EligibleRoomTypeIdsJson).HasMaxLength(2000);
            b.HasIndex(x => new { x.PropertyId, x.Code }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ── M10: Group Bookings ───────────────────────────────────────────────

        modelBuilder.Entity<GroupBooking>(b =>
        {
            b.ToTable("group_bookings");
            b.HasKey(x => x.Id);
            b.Property(x => x.CoordinatorName).HasMaxLength(200).IsRequired();
            b.Property(x => x.CoordinatorEmail).HasMaxLength(255);
            b.Property(x => x.CoordinatorPhone).HasMaxLength(50);
            b.Property(x => x.CompanyName).HasMaxLength(200);
            b.Property(x => x.AgreedRateNotes).HasMaxLength(500);
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.Property(x => x.CancellationReason).HasMaxLength(500);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ── M10.5: Service Items ──────────────────────────────────────────────

        modelBuilder.Entity<ServiceItem>(b =>
        {
            b.ToTable("service_items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.Price).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.Unit).HasMaxLength(50).IsRequired();
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ── M10.5: Packages ───────────────────────────────────────────────────

        modelBuilder.Entity<Package>(b =>
        {
            b.ToTable("packages");
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).HasMaxLength(200).IsRequired();
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.RateAddOn).HasPrecision(18, 4).IsRequired();
            b.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PackageId).OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        modelBuilder.Entity<PackageItem>(b =>
        {
            b.ToTable("package_items");
            b.HasKey(x => x.Id);
            b.Property(x => x.Notes).HasMaxLength(200);
        });

        // ── M10.5: Quotations ─────────────────────────────────────────────────

        modelBuilder.Entity<Quotation>(b =>
        {
            b.ToTable("quotations");
            b.HasKey(x => x.Id);
            b.Property(x => x.QuotationNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            b.Property(x => x.QuotedRatePerNight).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.TotalAmount).HasPrecision(18, 4).IsRequired();
            b.Property(x => x.Notes).HasMaxLength(1000);
            b.HasIndex(x => new { x.PropertyId, x.QuotationNumber }).IsUnique();
            b.HasIndex(x => x.OrganizationId);
            b.HasIndex(x => x.PropertyId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        // ── M11: Thai Compliance ──────────────────────────────────────────────

        modelBuilder.Entity<ThaiComplianceRecord>(b =>
        {
            b.ToTable("thai_compliance_records");
            b.HasKey(x => x.Id);
            b.Property(x => x.GuestFullName).HasMaxLength(200).IsRequired();
            b.Property(x => x.NationalId).HasMaxLength(20);
            b.Property(x => x.PassportNumber).HasMaxLength(50);
            b.Property(x => x.Nationality).HasMaxLength(100);
            b.Property(x => x.VisaType).HasMaxLength(50);
            b.Property(x => x.CountryOfOrigin).HasMaxLength(100);
            b.Property(x => x.RoomNumber).HasMaxLength(50);
            b.HasMany(x => x.Amendments).WithOne().HasForeignKey(x => x.RecordId)
                .OnDelete(DeleteBehavior.Cascade);
            b.HasIndex(x => new { x.PropertyId, x.ReservationId }).IsUnique();
            b.HasIndex(x => new { x.PropertyId, x.CheckInDate });
            b.HasIndex(x => new { x.PropertyId, x.IsForeigner });
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });

        modelBuilder.Entity<ThaiComplianceAmendment>(b =>
        {
            b.ToTable("thai_compliance_amendments");
            b.HasKey(x => x.Id);
            b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            b.Property(x => x.AmendedByEmail).HasMaxLength(255).IsRequired();
            b.Property(x => x.NewNationalId).HasMaxLength(20);
            b.Property(x => x.NewPassportNumber).HasMaxLength(50);
            b.Property(x => x.NewNationality).HasMaxLength(100);
            b.Property(x => x.NewVisaType).HasMaxLength(50);
            b.Property(x => x.NewCountryOfOrigin).HasMaxLength(100);
        });

        // ── M11: Guest Thai compliance fields ────────────────────────────────
        modelBuilder.Entity<Guest>(b =>
        {
            b.Property(x => x.DateOfBirth).IsRequired(false);
            b.Property(x => x.VisaType).HasMaxLength(50).IsRequired(false);
            b.Property(x => x.CountryOfOrigin).HasMaxLength(100).IsRequired(false);
        });

        // ── M11: Property Thai compliance flag ───────────────────────────────
        modelBuilder.Entity<Property>(b =>
        {
            b.Property(x => x.EnableThaiCompliance).HasDefaultValue(false);
        });

        // ── M12: Email Notification Settings ─────────────────────────────────
        modelBuilder.Entity<EmailNotificationSettings>(b =>
        {
            b.ToTable("email_notification_settings");
            b.HasKey(x => x.Id);
            b.Property(x => x.DailyReportRecipients).HasMaxLength(2000).IsRequired();
            b.HasIndex(x => x.PropertyId).IsUnique(); // one record per property
            b.HasIndex(x => x.OrganizationId);
            b.HasQueryFilter(x => !_tenantContext!.OrganizationId.HasValue || x.OrganizationId == _tenantContext.OrganizationId.Value);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var auditEntries = PrepareAuditAndTenantStamps();
        var result = await base.SaveChangesAsync(cancellationToken);

        if (auditEntries.Count > 0)
        {
            AuditEvents.AddRange(auditEntries);
            await base.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private List<AuditEvent> PrepareAuditAndTenantStamps()
    {
        var entries = new List<AuditEvent>();
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditEvent) continue;

            // Handle Tenant Entity Stamps
            if (entry.Entity is TenantEntity<Guid> tenantEntity && entry.State == EntityState.Added)
            {
                if (tenantEntity.OrganizationId == Guid.Empty && _tenantContext?.OrganizationId.HasValue == true)
                {
                    // Reflection / set backing field if needed
                    typeof(TenantEntity<Guid>)
                        .GetProperty(nameof(TenantEntity<Guid>.OrganizationId))?
                        .SetValue(tenantEntity, _tenantContext.OrganizationId.Value);
                }
            }

            // Handle Timestamps
            if (entry.State == EntityState.Added && entry.Entity is Entity<Guid> addedEntity)
            {
                typeof(Entity<Guid>)
                    .GetProperty(nameof(Entity<Guid>.CreatedAtUtc))?
                    .SetValue(addedEntity, now);
            }
            else if (entry.State == EntityState.Modified && entry.Entity is Entity<Guid> modifiedEntity)
            {
                modifiedEntity.Touch();
            }

            // Create Audit Events for business changes
            if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            {
                var entityName = entry.Metadata.ClrType.Name;
                var entityId = entry.Property("Id").CurrentValue?.ToString() ?? Guid.Empty.ToString();
                var action = entry.State.ToString();

                string? changes = null;
                if (entry.State == EntityState.Modified)
                {
                    var modifiedProps = entry.Properties
                        .Where(p => p.IsModified && !p.Metadata.IsPrimaryKey())
                        .ToDictionary(p => p.Metadata.Name, p => new
                        {
                            Old = p.OriginalValue,
                            New = p.CurrentValue
                        });

                    changes = JsonSerializer.Serialize(modifiedProps);
                }

                entries.Add(AuditEvent.Create(
                    organizationId: _tenantContext?.OrganizationId,
                    propertyId: _tenantContext?.PropertyId,
                    actorId: _tenantContext?.UserId,
                    actorEmail: _tenantContext?.UserEmail,
                    entityName: entityName,
                    entityId: entityId,
                    action: action,
                    changesJson: changes));
            }
        }

        return entries;
    }
}

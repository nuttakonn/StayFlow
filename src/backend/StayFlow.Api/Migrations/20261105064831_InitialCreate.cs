using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: true),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorEmail = table.Column<string>(type: "text", nullable: true),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EntityName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ChangesJson = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "branding_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    LogoUrl = table.Column<string>(type: "text", nullable: true),
                    FaviconUrl = table.Column<string>(type: "text", nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SecondaryColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LoginTitle = table.Column<string>(type: "text", nullable: true),
                    LoginWelcomeText = table.Column<string>(type: "text", nullable: true),
                    FooterText = table.Column<string>(type: "text", nullable: true),
                    SupportEmail = table.Column<string>(type: "text", nullable: true),
                    SupportPhone = table.Column<string>(type: "text", nullable: true),
                    ShowVendorAttribution = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branding_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NumberOfFloors = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buildings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "folios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolioNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "guests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PassportNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Nationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "maintenance_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReportedByEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AssignedToEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_maintenance_requests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "organizations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Slug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BusinessType = table.Column<int>(type: "integer", nullable: false),
                    IanaTimeZone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: true),
                    ContactPhone = table.Column<string>(type: "text", nullable: true),
                    ContactEmail = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CheckInTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CheckOutTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_properties", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "property_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationMembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_property_memberships", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GuestId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Adults = table.Column<int>(type: "integer", nullable: false),
                    Children = table.Column<int>(type: "integer", nullable: false),
                    QuotedRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SpecialRequests = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckedInAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CheckedOutAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RowVersion = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "room_types",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BaseRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StandardOccupancy = table.Column<int>(type: "integer", nullable: false),
                    MaxOccupancy = table.Column<int>(type: "integer", nullable: false),
                    MaxAdults = table.Column<int>(type: "integer", nullable: false),
                    MaxChildren = table.Column<int>(type: "integer", nullable: false),
                    AmenitiesJson = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_types", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    FloorId = table.Column<Guid>(type: "uuid", nullable: true),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    OperationalStatus = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ToValue = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_histories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: true),
                    MaxProperties = table.Column<int>(type: "integer", nullable: true),
                    TrialDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TrialEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    NationalIdOrPassport = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EmergencyContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "floors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    FloorNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_floors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_floors_buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "buildings",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "folio_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_folio_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_folio_items_folios_FolioId",
                        column: x => x.FolioId,
                        principalTable: "folios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ReversalReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_payments_folios_FolioId",
                        column: x => x.FolioId,
                        principalTable: "folios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "organization_memberships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_organization_memberships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_organization_memberships_organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reservation_status_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reservation_status_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_reservation_status_histories_reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meter_readings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    UtilityType = table.Column<int>(type: "integer", nullable: false),
                    ReadingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BillingMonth = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PreviousReading = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrentReading = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meter_readings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meter_readings_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "room_status_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: false),
                    ToStatus = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedByUserEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_room_status_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_room_status_histories_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_entitlements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubscriptionPlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    CapabilityKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_entitlements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plan_entitlements_subscription_plans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "subscription_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rental_agreements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgreementNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    MonthlyRent = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SecurityDeposit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositPaid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    BillingCycleDay = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TerminatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TerminationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DepositRefundedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    DepositDeductedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rental_agreements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rental_agreements_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_rental_agreements_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monthly_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AgreementId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    BillingMonth = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monthly_invoices_rental_agreements_AgreementId",
                        column: x => x.AgreementId,
                        principalTable: "rental_agreements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_monthly_invoices_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_payment_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_payment_records", x => x.Id);
                    table.ForeignKey(
                        name: "FK_invoice_payment_records_monthly_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "monthly_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "monthly_invoice_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemType = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_monthly_invoice_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_monthly_invoice_items_monthly_invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "monthly_invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_OrganizationId",
                table: "audit_events",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TimestampUtc",
                table: "audit_events",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_branding_settings_OrganizationId_PropertyId",
                table: "branding_settings",
                columns: new[] { "OrganizationId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_buildings_OrganizationId",
                table: "buildings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_buildings_PropertyId_Code",
                table: "buildings",
                columns: new[] { "PropertyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_floors_BuildingId",
                table: "floors",
                column: "BuildingId");

            migrationBuilder.CreateIndex(
                name: "IX_floors_OrganizationId",
                table: "floors",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_floors_PropertyId_BuildingId_FloorNumber",
                table: "floors",
                columns: new[] { "PropertyId", "BuildingId", "FloorNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_folio_items_FolioId",
                table: "folio_items",
                column: "FolioId");

            migrationBuilder.CreateIndex(
                name: "IX_folio_items_OrganizationId",
                table: "folio_items",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_folio_items_PropertyId",
                table: "folio_items",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_folios_OrganizationId",
                table: "folios",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_folios_PropertyId",
                table: "folios",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_folios_PropertyId_FolioNumber",
                table: "folios",
                columns: new[] { "PropertyId", "FolioNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_folios_ReservationId",
                table: "folios",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_guests_OrganizationId",
                table: "guests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_guests_OrganizationId_Email",
                table: "guests",
                columns: new[] { "OrganizationId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_records_InvoiceId",
                table: "invoice_payment_records",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_records_OrganizationId",
                table: "invoice_payment_records",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_invoice_payment_records_PropertyId",
                table: "invoice_payment_records",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_requests_OrganizationId",
                table: "maintenance_requests",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_requests_PropertyId",
                table: "maintenance_requests",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_requests_RoomId",
                table: "maintenance_requests",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_maintenance_requests_Status",
                table: "maintenance_requests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_OrganizationId",
                table: "meter_readings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_PropertyId",
                table: "meter_readings",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_PropertyId_RoomId_UtilityType_BillingMonth",
                table: "meter_readings",
                columns: new[] { "PropertyId", "RoomId", "UtilityType", "BillingMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meter_readings_RoomId",
                table: "meter_readings",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoice_items_InvoiceId",
                table: "monthly_invoice_items",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoice_items_OrganizationId",
                table: "monthly_invoice_items",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoice_items_PropertyId",
                table: "monthly_invoice_items",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_AgreementId",
                table: "monthly_invoices",
                column: "AgreementId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_OrganizationId",
                table: "monthly_invoices",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_PropertyId",
                table: "monthly_invoices",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_PropertyId_AgreementId_BillingMonth",
                table: "monthly_invoices",
                columns: new[] { "PropertyId", "AgreementId", "BillingMonth" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_RoomId",
                table: "monthly_invoices",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_monthly_invoices_TenantId",
                table: "monthly_invoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_OrganizationId_UserId",
                table: "organization_memberships",
                columns: new[] { "OrganizationId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_organization_memberships_UserId",
                table: "organization_memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_organizations_Slug",
                table: "organizations",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payments_CreatedAtUtc",
                table: "payments",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_payments_FolioId",
                table: "payments",
                column: "FolioId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_OrganizationId",
                table: "payments",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_payments_PropertyId",
                table: "payments",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_plan_entitlements_SubscriptionPlanId_CapabilityKey",
                table: "plan_entitlements",
                columns: new[] { "SubscriptionPlanId", "CapabilityKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_properties_OrganizationId",
                table: "properties",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_properties_OrganizationId_Code",
                table: "properties",
                columns: new[] { "OrganizationId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_property_memberships_OrganizationId",
                table: "property_memberships",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_property_memberships_OrganizationMembershipId_PropertyId",
                table: "property_memberships",
                columns: new[] { "OrganizationMembershipId", "PropertyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_property_memberships_PropertyId",
                table: "property_memberships",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_Token",
                table: "refresh_tokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_OrganizationId",
                table: "rental_agreements",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_PropertyId",
                table: "rental_agreements",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_PropertyId_AgreementNumber",
                table: "rental_agreements",
                columns: new[] { "PropertyId", "AgreementNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_RoomId",
                table: "rental_agreements",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_Status",
                table: "rental_agreements",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_rental_agreements_TenantId",
                table: "rental_agreements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_status_histories_CreatedAtUtc",
                table: "reservation_status_histories",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_status_histories_OrganizationId",
                table: "reservation_status_histories",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_reservation_status_histories_ReservationId",
                table: "reservation_status_histories",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_CheckInDate",
                table: "reservations",
                column: "CheckInDate");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_CheckOutDate",
                table: "reservations",
                column: "CheckOutDate");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_GuestId",
                table: "reservations",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_OrganizationId",
                table: "reservations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_PropertyId",
                table: "reservations",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_RoomId",
                table: "reservations",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_reservations_Status",
                table: "reservations",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_room_status_histories_CreatedAtUtc",
                table: "room_status_histories",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_room_status_histories_OrganizationId",
                table: "room_status_histories",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_room_status_histories_RoomId",
                table: "room_status_histories",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_room_types_OrganizationId",
                table: "room_types",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_room_types_PropertyId_Code",
                table: "room_types",
                columns: new[] { "PropertyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_OperationalStatus",
                table: "rooms",
                column: "OperationalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_rooms_OrganizationId",
                table: "rooms",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_rooms_PropertyId_RoomNumber",
                table: "rooms",
                columns: new[] { "PropertyId", "RoomNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_RoomTypeId",
                table: "rooms",
                column: "RoomTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_histories_OrganizationId",
                table: "subscription_histories",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_histories_SubscriptionId",
                table: "subscription_histories",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plans_Key",
                table: "subscription_plans",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_OrganizationId",
                table: "subscriptions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_SubscriptionPlanId",
                table: "subscriptions",
                column: "SubscriptionPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_OrganizationId",
                table: "tenants",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_PhoneNumber",
                table: "tenants",
                column: "PhoneNumber");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_PropertyId",
                table: "tenants",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "branding_settings");

            migrationBuilder.DropTable(
                name: "floors");

            migrationBuilder.DropTable(
                name: "folio_items");

            migrationBuilder.DropTable(
                name: "guests");

            migrationBuilder.DropTable(
                name: "invoice_payment_records");

            migrationBuilder.DropTable(
                name: "maintenance_requests");

            migrationBuilder.DropTable(
                name: "meter_readings");

            migrationBuilder.DropTable(
                name: "monthly_invoice_items");

            migrationBuilder.DropTable(
                name: "organization_memberships");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "plan_entitlements");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropTable(
                name: "property_memberships");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "reservation_status_histories");

            migrationBuilder.DropTable(
                name: "room_status_histories");

            migrationBuilder.DropTable(
                name: "room_types");

            migrationBuilder.DropTable(
                name: "subscription_histories");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "buildings");

            migrationBuilder.DropTable(
                name: "monthly_invoices");

            migrationBuilder.DropTable(
                name: "organizations");

            migrationBuilder.DropTable(
                name: "folios");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropTable(
                name: "reservations");

            migrationBuilder.DropTable(
                name: "rental_agreements");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "tenants");
        }
    }
}

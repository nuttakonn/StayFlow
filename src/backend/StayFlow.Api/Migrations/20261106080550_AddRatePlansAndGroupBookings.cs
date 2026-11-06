using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRatePlansAndGroupBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                table: "reservations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DiscountAmount",
                table: "reservations",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupBookingId",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RatePlanId",
                table: "reservations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DiscountType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MaxUses = table.Column<int>(type: "integer", nullable: true),
                    UsedCount = table.Column<int>(type: "integer", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    EligibleRoomTypeIdsJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coupons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "group_bookings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CoordinatorEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CoordinatorPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CompanyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AgreedRateNotes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CancellationReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_bookings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_plans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    BaseRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    RateType = table.Column<int>(type: "integer", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_plans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "rate_plan_restrictions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinNights = table.Column<int>(type: "integer", nullable: true),
                    MaxAdvanceDays = table.Column<int>(type: "integer", nullable: true),
                    BlackoutDatesJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_plan_restrictions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_plan_restrictions_rate_plans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "rate_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rate_rules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RatePlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayType = table.Column<int>(type: "integer", nullable: false),
                    RateMultiplier = table.Column<decimal>(type: "numeric(8,4)", precision: 8, scale: 4, nullable: true),
                    FlatAdjustment = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    SeasonStartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SeasonEndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rate_rules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_rate_rules_rate_plans_RatePlanId",
                        column: x => x.RatePlanId,
                        principalTable: "rate_plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_reservations_GroupBookingId",
                table: "reservations",
                column: "GroupBookingId");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_OrganizationId",
                table: "coupons",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_coupons_PropertyId_Code",
                table: "coupons",
                columns: new[] { "PropertyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_bookings_OrganizationId",
                table: "group_bookings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_group_bookings_PropertyId",
                table: "group_bookings",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_plan_restrictions_RatePlanId",
                table: "rate_plan_restrictions",
                column: "RatePlanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_plans_OrganizationId",
                table: "rate_plans",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_rate_plans_PropertyId_Code",
                table: "rate_plans",
                columns: new[] { "PropertyId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rate_rules_RatePlanId",
                table: "rate_rules",
                column: "RatePlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupons");

            migrationBuilder.DropTable(
                name: "group_bookings");

            migrationBuilder.DropTable(
                name: "rate_plan_restrictions");

            migrationBuilder.DropTable(
                name: "rate_rules");

            migrationBuilder.DropTable(
                name: "rate_plans");

            migrationBuilder.DropIndex(
                name: "IX_reservations_GroupBookingId",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "DiscountAmount",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "GroupBookingId",
                table: "reservations");

            migrationBuilder.DropColumn(
                name: "RatePlanId",
                table: "reservations");
        }
    }
}

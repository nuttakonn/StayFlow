using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookingEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_engine_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    HoldDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    BookingPolicyText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    MinAdvanceBookingDays = table.Column<int>(type: "integer", nullable: false),
                    MaxAdvanceBookingDays = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_engine_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "booking_holds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionToken = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Adults = table.Column<int>(type: "integer", nullable: false),
                    Children = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConvertedReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    GuestEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_holds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_engine_settings_OrganizationId",
                table: "booking_engine_settings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_booking_engine_settings_PropertyId",
                table: "booking_engine_settings",
                column: "PropertyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_holds_ExpiresAtUtc",
                table: "booking_holds",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_booking_holds_PropertyId_RoomTypeId_Status",
                table: "booking_holds",
                columns: new[] { "PropertyId", "RoomTypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_booking_holds_SessionToken",
                table: "booking_holds",
                column: "SessionToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_engine_settings");

            migrationBuilder.DropTable(
                name: "booking_holds");
        }
    }
}

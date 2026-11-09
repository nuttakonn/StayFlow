using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_notification_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SendBookingConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    SendDailyReport = table.Column<bool>(type: "boolean", nullable: false),
                    DailyReportRecipients = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DailyReportSendHourUtc = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_notification_settings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_notification_settings_OrganizationId",
                table: "email_notification_settings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_email_notification_settings_PropertyId",
                table: "email_notification_settings",
                column: "PropertyId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_notification_settings");
        }
    }
}

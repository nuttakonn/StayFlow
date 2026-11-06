using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelManager : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "channel_connections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    ProviderPropertyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EncryptedApiKey = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EncryptedApiSecret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastSyncAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_connections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_rate_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelRoomTypeCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ChannelRatePlanCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_rate_mappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "channel_reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelConnectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelBookingReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OtaSource = table.Column<int>(type: "integer", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawPayload = table.Column<string>(type: "text", nullable: false),
                    ProcessingStatus = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_reservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_OrganizationId",
                table: "channel_connections",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_PropertyId",
                table: "channel_connections",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_connections_PropertyId_Provider",
                table: "channel_connections",
                columns: new[] { "PropertyId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_rate_mappings_ChannelConnectionId",
                table: "channel_rate_mappings",
                column: "ChannelConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_rate_mappings_ChannelConnectionId_RoomTypeId",
                table: "channel_rate_mappings",
                columns: new[] { "ChannelConnectionId", "RoomTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_rate_mappings_OrganizationId",
                table: "channel_rate_mappings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_reservations_ChannelConnectionId",
                table: "channel_reservations",
                column: "ChannelConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_reservations_ChannelConnectionId_ChannelBookingRefe~",
                table: "channel_reservations",
                columns: new[] { "ChannelConnectionId", "ChannelBookingReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_reservations_OrganizationId",
                table: "channel_reservations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_reservations_ProcessingStatus",
                table: "channel_reservations",
                column: "ProcessingStatus");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "channel_connections");

            migrationBuilder.DropTable(
                name: "channel_rate_mappings");

            migrationBuilder.DropTable(
                name: "channel_reservations");
        }
    }
}

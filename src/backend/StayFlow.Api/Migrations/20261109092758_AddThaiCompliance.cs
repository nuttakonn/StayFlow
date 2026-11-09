using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddThaiCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableThaiCompliance",
                table: "properties",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CountryOfOrigin",
                table: "guests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                table: "guests",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisaType",
                table: "guests",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "thai_compliance_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuestId = table.Column<Guid>(type: "uuid", nullable: false),
                    GuestFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NationalId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PassportNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Nationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    VisaType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CountryOfOrigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsForeigner = table.Column<bool>(type: "boolean", nullable: false),
                    RoomNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CheckInDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CheckOutDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Adults = table.Column<int>(type: "integer", nullable: false),
                    IsAmended = table.Column<bool>(type: "boolean", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thai_compliance_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "thai_compliance_amendments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AmendedByEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    PrevNationalId = table.Column<string>(type: "text", nullable: true),
                    NewNationalId = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    PrevPassportNumber = table.Column<string>(type: "text", nullable: true),
                    NewPassportNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PrevNationality = table.Column<string>(type: "text", nullable: true),
                    NewNationality = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PrevDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    NewDateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    PrevVisaType = table.Column<string>(type: "text", nullable: true),
                    NewVisaType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PrevCountryOfOrigin = table.Column<string>(type: "text", nullable: true),
                    NewCountryOfOrigin = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_thai_compliance_amendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_thai_compliance_amendments_thai_compliance_records_RecordId",
                        column: x => x.RecordId,
                        principalTable: "thai_compliance_records",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_thai_compliance_amendments_RecordId",
                table: "thai_compliance_amendments",
                column: "RecordId");

            migrationBuilder.CreateIndex(
                name: "IX_thai_compliance_records_OrganizationId",
                table: "thai_compliance_records",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_thai_compliance_records_PropertyId_CheckInDate",
                table: "thai_compliance_records",
                columns: new[] { "PropertyId", "CheckInDate" });

            migrationBuilder.CreateIndex(
                name: "IX_thai_compliance_records_PropertyId_IsForeigner",
                table: "thai_compliance_records",
                columns: new[] { "PropertyId", "IsForeigner" });

            migrationBuilder.CreateIndex(
                name: "IX_thai_compliance_records_PropertyId_ReservationId",
                table: "thai_compliance_records",
                columns: new[] { "PropertyId", "ReservationId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "thai_compliance_amendments");

            migrationBuilder.DropTable(
                name: "thai_compliance_records");

            migrationBuilder.DropColumn(
                name: "EnableThaiCompliance",
                table: "properties");

            migrationBuilder.DropColumn(
                name: "CountryOfOrigin",
                table: "guests");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "guests");

            migrationBuilder.DropColumn(
                name: "VisaType",
                table: "guests");
        }
    }
}

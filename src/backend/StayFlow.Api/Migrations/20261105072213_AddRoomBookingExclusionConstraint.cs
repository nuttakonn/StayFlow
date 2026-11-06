using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StayFlow.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomBookingExclusionConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // btree_gist is required for mixing equality (=) and range (&&) operators in one EXCLUDE clause.
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

            // Prevent two active reservations from being assigned to the same room for overlapping dates.
            // Half-open interval [CheckInDate, CheckOutDate) mirrors the domain rule in Reservation.OverlapsWith.
            // Cancelled (Status=5) and NoShow (Status=6) reservations are excluded so they don't block re-booking.
            migrationBuilder.Sql("""
                ALTER TABLE reservations
                ADD CONSTRAINT no_room_double_booking
                EXCLUDE USING gist (
                    "PropertyId"   WITH =,
                    "RoomId"       WITH =,
                    daterange("CheckInDate", "CheckOutDate", '[)') WITH &&
                )
                WHERE ("RoomId" IS NOT NULL AND "Status" NOT IN (5, 6));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE reservations DROP CONSTRAINT IF EXISTS no_room_double_booking;");
        }
    }
}

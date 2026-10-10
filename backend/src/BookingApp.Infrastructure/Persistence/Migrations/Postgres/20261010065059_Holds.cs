using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Holds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "hold_expires_at",
                table: "bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_bookings_status_hold_expires",
                table: "bookings",
                columns: new[] { "status", "hold_expires_at" });

            // Holds block the slot as well: the database refuses an overlap with a confirmed booking or a hold.
            // Expired holds are flipped to Expired before each insert (EfBookingRepository), so they never block.
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT ex_bookings_no_overlap;");
            migrationBuilder.Sql(
                "ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap " +
                "EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (status IN ('Confirmed', 'Pending'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE bookings DROP CONSTRAINT ex_bookings_no_overlap;");
            migrationBuilder.Sql(
                "ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap " +
                "EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (status = 'Confirmed');");

            migrationBuilder.DropIndex(
                name: "ix_bookings_status_hold_expires",
                table: "bookings");

            migrationBuilder.DropColumn(
                name: "hold_expires_at",
                table: "bookings");
        }
    }
}

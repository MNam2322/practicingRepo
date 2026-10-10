using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class QueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_resource_start",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_resource_start",
                table: "bookings",
                columns: new[] { "resource_id", "start_utc" },
                filter: "status IN ('Confirmed', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_start",
                table: "bookings",
                column: "start_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_bookings_resource_start",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_start",
                table: "bookings");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_resource_start",
                table: "bookings",
                columns: new[] { "resource_id", "start_utc" });
        }
    }
}

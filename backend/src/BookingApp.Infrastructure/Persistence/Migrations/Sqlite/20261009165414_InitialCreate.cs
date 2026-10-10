using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    resource_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    start_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    end_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    customer_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    customer_name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    cancelled_at = table.Column<long>(type: "INTEGER", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_end_after_start", "end_utc > start_utc");
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer",
                table: "bookings",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_resource_start",
                table: "bookings",
                columns: new[] { "resource_id", "start_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");
        }
    }
}

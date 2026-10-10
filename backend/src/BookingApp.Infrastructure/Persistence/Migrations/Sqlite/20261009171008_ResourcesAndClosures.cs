using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class ResourcesAndClosures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    slot_minutes = table.Column<int>(type: "INTEGER", nullable: false),
                    max_slots_per_booking = table.Column<int>(type: "INTEGER", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resources", x => x.id);
                    table.CheckConstraint("ck_resources_capacity", "capacity > 0");
                    table.CheckConstraint("ck_resources_kind", "kind IN ('Court', 'Room', 'Table', 'Other')");
                    table.CheckConstraint("ck_resources_max_slots", "max_slots_per_booking >= 1");
                    table.CheckConstraint("ck_resources_slot_minutes", "slot_minutes > 0 AND slot_minutes <= 1440");
                });

            migrationBuilder.CreateTable(
                name: "venue",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    time_zone_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue", x => x.id);
                    table.CheckConstraint("ck_venue_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    resource_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    start_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    end_utc = table.Column<long>(type: "INTEGER", nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_closures", x => x.id);
                    table.CheckConstraint("ck_closures_end_after_start", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "FK_closures_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opening_hours",
                columns: table => new
                {
                    resource_id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    day_of_week = table.Column<int>(type: "INTEGER", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_opening_hours", x => new { x.resource_id, x.day_of_week });
                    table.CheckConstraint("ck_opening_hours_close_after_open", "close_time > open_time");
                    table.CheckConstraint("ck_opening_hours_day", "day_of_week BETWEEN 0 AND 6");
                    table.ForeignKey(
                        name: "FK_opening_hours_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_closures_resource_start",
                table: "closures",
                columns: new[] { "resource_id", "start_utc" });

            // Expand before contract: bookings made before this migration point at resources that only lived in
            // configuration. Give each one a placeholder row so the foreign key can be added; on first start the
            // DatabaseInitializer overwrites placeholders with the real settings (library 7.5, 7.7 "read every migration").
            migrationBuilder.Sql(
                "INSERT INTO resources (id, name, kind, capacity, slot_minutes, max_slots_per_booking, sort_order) " +
                "SELECT DISTINCT resource_id, resource_id, 'Other', 1, 60, 1, 999 FROM bookings " +
                "WHERE resource_id NOT IN (SELECT id FROM resources);");

            migrationBuilder.AddForeignKey(
                name: "FK_bookings_resources_resource_id",
                table: "bookings",
                column: "resource_id",
                principalTable: "resources",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_bookings_resources_resource_id",
                table: "bookings");

            migrationBuilder.DropTable(
                name: "closures");

            migrationBuilder.DropTable(
                name: "opening_hours");

            migrationBuilder.DropTable(
                name: "venue");

            migrationBuilder.DropTable(
                name: "resources");
        }
    }
}

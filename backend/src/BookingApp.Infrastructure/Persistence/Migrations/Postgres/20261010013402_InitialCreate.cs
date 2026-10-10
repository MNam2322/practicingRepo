using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    slot_minutes = table.Column<int>(type: "integer", nullable: false),
                    max_slots_per_booking = table.Column<int>(type: "integer", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
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
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venue", x => x.id);
                    table.CheckConstraint("ck_venue_single_row", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    period = table.Column<NpgsqlRange<DateTime>>(type: "tstzrange", nullable: false, computedColumnSql: "tstzrange(start_utc, end_utc, '[)')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookings", x => x.id);
                    table.CheckConstraint("ck_bookings_end_after_start", "end_utc > start_utc");
                    table.ForeignKey(
                        name: "FK_bookings_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "closures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
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
                name: "ix_bookings_customer",
                table: "bookings",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_bookings_resource_start",
                table: "bookings",
                columns: new[] { "resource_id", "start_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_closures_resource_start",
                table: "closures",
                columns: new[] { "resource_id", "start_utc" });

            // The rule that matters most, enforced by the database for every writer: no two confirmed bookings for the
            // same resource may overlap. GiST supports the range overlap operator (&&); btree_gist adds = for
            // resource_id. A violating INSERT fails with SQLSTATE 23P01, which EfBookingRepository maps to slot_taken.
            // EF Core has no fluent API for EXCLUDE, so it is written here (library 7.2, 7.3 "Specialised index types").
            migrationBuilder.Sql(
                "ALTER TABLE bookings ADD CONSTRAINT ex_bookings_no_overlap " +
                "EXCLUDE USING gist (resource_id WITH =, period WITH &&) WHERE (status = 'Confirmed');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bookings");

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

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class AdminAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "cancellation_cutoff_hours",
                table: "venue",
                type: "double precision",
                nullable: false,
                defaultValue: 2.0);

            migrationBuilder.AddColumn<int>(
                name: "max_active_bookings",
                table: "venue",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "max_days_ahead",
                table: "venue",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "min_lead_minutes",
                table: "venue",
                type: "integer",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    before = table.Column<string>(type: "text", nullable: true),
                    after = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_venue_cancellation_cutoff",
                table: "venue",
                sql: "cancellation_cutoff_hours BETWEEN 0 AND 168");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venue_max_active",
                table: "venue",
                sql: "max_active_bookings BETWEEN 1 AND 100");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venue_max_days_ahead",
                table: "venue",
                sql: "max_days_ahead BETWEEN 1 AND 365");

            migrationBuilder.AddCheckConstraint(
                name: "ck_venue_min_lead_minutes",
                table: "venue",
                sql: "min_lead_minutes BETWEEN 0 AND 1440");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity",
                table: "audit_log",
                columns: new[] { "entity", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "occurred_at");

            // Append-only, enforced by the database for every writer (library 10.6 "Auditing and compliance").
            migrationBuilder.Sql("CREATE FUNCTION audit_log_append_only() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'audit_log is append-only'; END; $$;");
            migrationBuilder.Sql("CREATE TRIGGER audit_log_no_change BEFORE UPDATE OR DELETE ON audit_log FOR EACH ROW EXECUTE FUNCTION audit_log_append_only();");
            migrationBuilder.Sql("CREATE TRIGGER audit_log_no_truncate BEFORE TRUNCATE ON audit_log FOR EACH STATEMENT EXECUTE FUNCTION audit_log_append_only();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_change ON audit_log;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS audit_log_no_truncate ON audit_log;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_log_append_only();");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venue_cancellation_cutoff",
                table: "venue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venue_max_active",
                table: "venue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venue_max_days_ahead",
                table: "venue");

            migrationBuilder.DropCheckConstraint(
                name: "ck_venue_min_lead_minutes",
                table: "venue");

            migrationBuilder.DropColumn(
                name: "cancellation_cutoff_hours",
                table: "venue");

            migrationBuilder.DropColumn(
                name: "max_active_bookings",
                table: "venue");

            migrationBuilder.DropColumn(
                name: "max_days_ahead",
                table: "venue");

            migrationBuilder.DropColumn(
                name: "min_lead_minutes",
                table: "venue");
        }
    }
}

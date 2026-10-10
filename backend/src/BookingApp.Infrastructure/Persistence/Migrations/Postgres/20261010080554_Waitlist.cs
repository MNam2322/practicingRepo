using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Waitlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_kind",
                table: "notification_outbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "notification_outbox",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "body",
                table: "notification_outbox",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subject",
                table: "notification_outbox",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "waitlist",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    start_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist", x => x.id);
                    table.CheckConstraint("ck_waitlist_end_after_start", "end_utc > start_utc");
                    table.CheckConstraint("ck_waitlist_status", "status IN ('Waiting', 'Notified', 'Left', 'Expired')");
                    table.ForeignKey(
                        name: "FK_waitlist_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_about",
                table: "notification_outbox",
                sql: "booking_id IS NOT NULL OR (subject IS NOT NULL AND body IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_kind",
                table: "notification_outbox",
                sql: "kind IN ('BookingConfirmed', 'BookingCancelled', 'Reminder', 'WaitlistSlotFree')");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_customer",
                table: "waitlist",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_open",
                table: "waitlist",
                columns: new[] { "status", "start_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_waitlist_one_wait",
                table: "waitlist",
                columns: new[] { "resource_id", "start_utc", "end_utc", "customer_id" },
                unique: true,
                filter: "status = 'Waiting'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waitlist");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_about",
                table: "notification_outbox");

            migrationBuilder.DropCheckConstraint(
                name: "ck_outbox_kind",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "body",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "subject",
                table: "notification_outbox");

            migrationBuilder.AlterColumn<Guid>(
                name: "booking_id",
                table: "notification_outbox",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_outbox_kind",
                table: "notification_outbox",
                sql: "kind IN ('BookingConfirmed', 'BookingCancelled', 'Reminder')");
        }
    }
}

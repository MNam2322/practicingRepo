using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Payments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_events",
                columns: table => new
                {
                    event_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    payment_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    received_at = table.Column<long>(type: "INTEGER", nullable: false),
                    payload = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    booking_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    amount_minor = table.Column<long>(type: "INTEGER", nullable: false),
                    currency = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    provider_reference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    failure_code = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "INTEGER", nullable: false),
                    paid_at = table.Column<long>(type: "INTEGER", nullable: true),
                    refunded_at = table.Column<long>(type: "INTEGER", nullable: true),
                    version = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount_minor > 0");
                    table.CheckConstraint("ck_payments_status", "status IN ('Pending', 'Paid', 'Failed', 'Refunded')");
                    table.ForeignKey(
                        name: "FK_payments_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payments_booking",
                table: "payments",
                column: "booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status",
                table: "payments",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_payments_one_live",
                table: "payments",
                column: "booking_id",
                unique: true,
                filter: "status IN ('Pending', 'Paid')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_events");

            migrationBuilder.DropTable(
                name: "payments");
        }
    }
}

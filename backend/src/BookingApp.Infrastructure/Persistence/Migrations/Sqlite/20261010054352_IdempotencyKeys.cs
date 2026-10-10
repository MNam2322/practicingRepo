using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookingApp.Infrastructure.Persistence.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class IdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                columns: table => new
                {
                    scope = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    key = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    claimed_at = table.Column<long>(type: "INTEGER", nullable: false),
                    response_status = table.Column<int>(type: "INTEGER", nullable: true),
                    response_body = table.Column<string>(type: "TEXT", nullable: true),
                    response_content_type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    response_location = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.scope, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_keys_claimed_at",
                table: "idempotency_keys",
                column: "claimed_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_keys");
        }
    }
}

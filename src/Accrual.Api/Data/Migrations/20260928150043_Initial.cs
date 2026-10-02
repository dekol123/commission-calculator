using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Accrual.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispatch_token = table.Column<Guid>(type: "uuid", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    user_external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    profit = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    next_calculation_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    calculation_attempts = table.Column<int>(type: "integer", nullable: false),
                    calculation_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "schema_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    schema_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schema_settings", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    beneficiary_external_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(19,2)", precision: 19, scale: 2, nullable: false),
                    schema_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_paid = table.Column<bool>(type: "boolean", nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_commissions_events_event_id",
                        column: x => x.event_id,
                        principalTable: "profit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "schema_settings",
                columns: new[] { "id", "schema_type" },
                values: new object[] { 1, "Linear" });

            migrationBuilder.CreateIndex(
                name: "ix_commissions_event_id_level",
                table: "commissions",
                columns: new[] { "event_id", "level" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commissions_payout_id",
                table: "commissions",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_message_id",
                table: "outbox_messages",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_status_next_attempt_at",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_profit_events_external_id",
                table: "profit_events",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profit_events_status_next_calculation_at",
                table: "profit_events",
                columns: new[] { "status", "next_calculation_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commissions");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "schema_settings");

            migrationBuilder.DropTable(
                name: "profit_events");
        }
    }
}

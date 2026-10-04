using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Wallet.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayoutBatchLease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payout_batches",
                columns: table => new
                {
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lease_token = table.Column<Guid>(type: "uuid", nullable: true),
                    locked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claim_failures = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_batches", x => x.payout_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_payout_id",
                table: "inbox_messages",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_batches_lease_token_next_attempt_at",
                table: "payout_batches",
                columns: new[] { "lease_token", "next_attempt_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payout_batches");

            migrationBuilder.DropIndex(
                name: "ix_inbox_messages_payout_id",
                table: "inbox_messages");
        }
    }
}

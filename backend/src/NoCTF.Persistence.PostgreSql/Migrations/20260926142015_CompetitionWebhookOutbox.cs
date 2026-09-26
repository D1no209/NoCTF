using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionWebhookOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_webhook_deliveries",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    payload_state = table.Column<short>(type: "smallint", nullable: false),
                    domain_event_created_at = table.Column<long>(type: "bigint", nullable: false),
                    outbox_persisted_at = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    next_retry_at = table.Column<long>(type: "bigint", nullable: false),
                    enqueue_lease_until = table.Column<long>(type: "bigint", nullable: true),
                    lease_expires_at = table.Column<long>(type: "bigint", nullable: true),
                    worker_dequeued_at = table.Column<long>(type: "bigint", nullable: true),
                    public_projection_ready_at = table.Column<long>(type: "bigint", nullable: true),
                    captured_at = table.Column<long>(type: "bigint", nullable: true),
                    first_http_attempt_started_at = table.Column<long>(type: "bigint", nullable: true),
                    last_http_attempt_started_at = table.Column<long>(type: "bigint", nullable: true),
                    last_http_attempt_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    projection_retry_count = table.Column<int>(type: "integer", nullable: false),
                    http_retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_http_status_code = table.Column<int>(type: "integer", nullable: true),
                    dead_letter_reason = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_deliveries", x => new { x.event_id, x.target_id });
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_frozen_projections",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frozen_at = table.Column<long>(type: "bigint", nullable: false),
                    captured_at = table.Column<long>(type: "bigint", nullable: false),
                    source_event_sequence_through = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_frozen_projections", x => new { x.competition_id, x.frozen_at });
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_outbox_events",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    domain_event_created_at = table.Column<long>(type: "bigint", nullable: false),
                    outbox_persisted_at = table.Column<long>(type: "bigint", nullable: false),
                    next_dispatch_at = table.Column<long>(type: "bigint", nullable: false),
                    worker_dequeued_at = table.Column<long>(type: "bigint", nullable: true),
                    dispatch_completed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_outbox_events", x => x.sequence);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_deliveries_competition_id_state_next_re",
                table: "competition_webhook_deliveries",
                columns: new[] { "competition_id", "state", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_outbox_events_competition_id_next_dispa",
                table: "competition_webhook_outbox_events",
                columns: new[] { "competition_id", "next_dispatch_at", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_outbox_events_event_id",
                table: "competition_webhook_outbox_events",
                column: "event_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "competition_webhook_frozen_projections");

            migrationBuilder.DropTable(
                name: "competition_webhook_outbox_events");
        }
    }
}

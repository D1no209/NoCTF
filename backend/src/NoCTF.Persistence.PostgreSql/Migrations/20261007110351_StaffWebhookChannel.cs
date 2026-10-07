using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StaffWebhookChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_business_sequence = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    competition_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    dispatched_at = table.Column<long>(type: "bigint", nullable: true),
                    change_kind = table.Column<short>(type: "smallint", nullable: true),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_reason = table.Column<short>(type: "smallint", nullable: true),
                    as_of_sequence = table.Column<long>(type: "bigint", nullable: true),
                    page_index = table.Column<int>(type: "integer", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_streams",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_business_sequence = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_streams", x => x.competition_id);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    authorization_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    previous_secret_valid_until = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    next_heartbeat_at = table.Column<long>(type: "bigint", nullable: false),
                    failure_since = table.Column<long>(type: "bigint", nullable: true),
                    needs_resync = table.Column<bool>(type: "boolean", nullable: false),
                    active_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    synced_through = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_targets_competitions_competition_",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_targets_users_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_work_items",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_kind = table.Column<short>(type: "smallint", nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_cheat_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_consultation_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_appeal_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_requires_staff_action = table.Column<bool>(type: "boolean", nullable: false),
                    summary_action_required_since = table.Column<long>(type: "bigint", nullable: true),
                    summary_created_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_updated_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_detected_at = table.Column<long>(type: "bigint", nullable: true),
                    summary_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_related_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_related_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_challenge_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_direction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    summary_reason_code = table.Column<short>(type: "smallint", nullable: true),
                    summary_subject = table.Column<short>(type: "smallint", nullable: true),
                    summary_actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_last_changed_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_work_items", x => new { x.competition_id, x.kind, x.item_id });
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_event_items",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    summary_kind = table.Column<short>(type: "smallint", nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_cheat_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_consultation_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_appeal_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_requires_staff_action = table.Column<bool>(type: "boolean", nullable: false),
                    summary_action_required_since = table.Column<long>(type: "bigint", nullable: true),
                    summary_created_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_updated_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_detected_at = table.Column<long>(type: "bigint", nullable: true),
                    summary_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_related_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_related_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_challenge_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_direction = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    summary_reason_code = table.Column<short>(type: "smallint", nullable: true),
                    summary_subject = table.Column<short>(type: "smallint", nullable: true),
                    summary_actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_last_changed_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_event_items", x => new { x.event_id, x.position });
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_event_items_competition_staff_web",
                        column: x => x.event_id,
                        principalTable: "competition_staff_webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_deliveries",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    next_attempt_at = table.Column<long>(type: "bigint", nullable: false),
                    lease_until = table.Column<long>(type: "bigint", nullable: true),
                    attempt_token = table.Column<Guid>(type: "uuid", nullable: false),
                    prepared_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_deliveries", x => new { x.event_id, x.target_id });
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_deliveries_competition_staff_webh",
                        column: x => x.event_id,
                        principalTable: "competition_staff_webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_deliveries_competition_staff_webh1",
                        column: x => x.target_id,
                        principalTable: "competition_staff_webhook_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_webhook_category",
                columns: table => new
                {
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_webhook_category", x => new { x.target_id, x.kind });
                    table.ForeignKey(
                        name: "fk_staff_webhook_category_competition_staff_webhook_targets_ta",
                        column: x => x.target_id,
                        principalTable: "competition_staff_webhook_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_deliveries_state_next_attempt_at",
                table: "competition_staff_webhook_deliveries",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_deliveries_target_id",
                table: "competition_staff_webhook_deliveries",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_events_competition_id_sequence",
                table: "competition_staff_webhook_events",
                columns: new[] { "competition_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_targets_approved_by_id",
                table: "competition_staff_webhook_targets",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_targets_competition_id_deleted",
                table: "competition_staff_webhook_targets",
                columns: new[] { "competition_id", "deleted" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_staff_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_event_items");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_streams");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_work_items");

            migrationBuilder.DropTable(
                name: "staff_webhook_category");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_events");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_targets");
        }
    }
}

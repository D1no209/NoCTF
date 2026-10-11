using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AddTheoryQuestionBankAndPapers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "theory_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    opens_at = table.Column<long>(type: "bigint", nullable: false),
                    closes_at = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<long>(type: "bigint", nullable: true),
                    finished_at = table.Column<long>(type: "bigint", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    attempt_limit = table.Column<int>(type: "integer", nullable: true),
                    scoring_mode = table.Column<short>(type: "smallint", nullable: false),
                    issuance_stopped = table.Column<bool>(type: "boolean", nullable: false),
                    allow_exposed_questions = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_activities_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competition_theory_activities",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_theory_activities", x => x.competition_id);
                    table.ForeignKey(
                        name: "fk_competition_theory_activities_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_theory_activities_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_activity_pauses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_activity_pauses", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_activity_pauses_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_draw_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_draw_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_draw_rules_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "theory_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_participants_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_participants_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_exposures",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_exposed_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_exposures", x => new { x.source_id, x.activity_id });
                    table.ForeignKey(
                        name: "fk_theory_question_exposures_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_question_exposures_theory_question_source_source_id",
                        column: x => x.source_id,
                        principalTable: "theory_question_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_questions_theory_question_source_source_id",
                        column: x => x.source_id,
                        principalTable: "theory_question_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_questions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_draw_rule_filters",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_draw_rule_filters", x => new { x.rule_id, x.kind, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_theory_draw_rule_filters_theory_draw_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "theory_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "theory_papers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    opened_at = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<long>(type: "bigint", nullable: true),
                    submission_kind = table.Column<short>(type: "smallint", nullable: true),
                    submitted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_papers", x => x.id);
                    table.UniqueConstraint("ak_theory_papers_participant_id_id", x => new { x.participant_id, x.id });
                    table.ForeignKey(
                        name: "fk_theory_papers_theory_participants_participant_id",
                        column: x => x.participant_id,
                        principalTable: "theory_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_papers_users_submitted_by_id",
                        column: x => x.submitted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_grants",
                columns: table => new
                {
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_grants", x => new { x.question_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_theory_question_grants_theory_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "theory_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_theory_question_grants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    explanation = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_question_revisions_theory_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "theory_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_active_papers",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_active_papers", x => x.participant_id);
                    table.ForeignKey(
                        name: "fk_theory_active_papers_theory_papers_participant_id_paper_id",
                        columns: x => new { x.participant_id, x.paper_id },
                        principalTable: "theory_papers",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_draw_receipts",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_draw_receipts", x => new { x.participant_id, x.source_id });
                    table.ForeignKey(
                        name: "fk_theory_draw_receipts_theory_papers_participant_id_paper_id",
                        columns: x => new { x.participant_id, x.paper_id },
                        principalTable: "theory_papers",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_draw_receipts_theory_question_source_source_id",
                        column: x => x.source_id,
                        principalTable: "theory_question_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_activity_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_activity_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_activity_operations_theory_activities_activity_id",
                        column: x => x.activity_id,
                        principalTable: "theory_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_activity_operations_theory_papers_paper_id",
                        column: x => x.paper_id,
                        principalTable: "theory_papers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_activity_operations_theory_question_revisions_revisi",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_activity_operations_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_draw_candidates",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_draw_candidates", x => new { x.rule_id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_theory_draw_candidates_theory_draw_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "theory_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_theory_draw_candidates_theory_question_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_draw_candidates_theory_question_source_source_id",
                        column: x => x.source_id,
                        principalTable: "theory_question_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_paper_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    paper_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    answered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    answered_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_paper_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_paper_items_theory_draw_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "theory_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_paper_items_theory_papers_paper_id",
                        column: x => x.paper_id,
                        principalTable: "theory_papers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_paper_items_theory_question_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_paper_items_users_answered_by_id",
                        column: x => x.answered_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_classifications",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_classifications", x => new { x.revision_id, x.kind, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_theory_question_classifications_theory_question_revisions_r",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_files",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_files", x => new { x.revision_id, x.file_id });
                    table.ForeignKey(
                        name: "fk_theory_question_files_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_question_files_theory_question_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_question_options",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    truth_value = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_question_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_theory_question_options_theory_question_revisions_revision_",
                        column: x => x.revision_id,
                        principalTable: "theory_question_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_answer_receipts",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resulting_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    saved_at = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_answer_receipts", x => new { x.item_id, x.request_id });
                    table.ForeignKey(
                        name: "fk_theory_answer_receipts_theory_paper_items_item_id",
                        column: x => x.item_id,
                        principalTable: "theory_paper_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_answer_receipts_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theory_paper_options",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    selected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theory_paper_options", x => new { x.item_id, x.option_id });
                    table.ForeignKey(
                        name: "fk_theory_paper_options_theory_paper_items_item_id",
                        column: x => x.item_id,
                        principalTable: "theory_paper_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_theory_paper_options_theory_question_option_option_id",
                        column: x => x.option_id,
                        principalTable: "theory_question_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_theory_activities_activity_id",
                table: "competition_theory_activities",
                column: "activity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_active_papers_paper_id",
                table: "theory_active_papers",
                column: "paper_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_active_papers_participant_id_paper_id",
                table: "theory_active_papers",
                columns: new[] { "participant_id", "paper_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_activities_owner_id",
                table: "theory_activities",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_activities_state_closes_at",
                table: "theory_activities",
                columns: new[] { "state", "closes_at" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_activity_operations_activity_id",
                table: "theory_activity_operations",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_activity_operations_actor_id",
                table: "theory_activity_operations",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_activity_operations_paper_id",
                table: "theory_activity_operations",
                column: "paper_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_activity_operations_revision_id",
                table: "theory_activity_operations",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_activity_pauses_activity_id_started_at",
                table: "theory_activity_pauses",
                columns: new[] { "activity_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_answer_receipts_actor_id",
                table: "theory_answer_receipts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_draw_candidates_revision_id",
                table: "theory_draw_candidates",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_draw_candidates_source_id",
                table: "theory_draw_candidates",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_draw_receipts_participant_id_paper_id",
                table: "theory_draw_receipts",
                columns: new[] { "participant_id", "paper_id" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_draw_receipts_source_id",
                table: "theory_draw_receipts",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_draw_rules_activity_id_order",
                table: "theory_draw_rules",
                columns: new[] { "activity_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_items_answered_by_id",
                table: "theory_paper_items",
                column: "answered_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_items_paper_id_order",
                table: "theory_paper_items",
                columns: new[] { "paper_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_items_paper_id_revision_id",
                table: "theory_paper_items",
                columns: new[] { "paper_id", "revision_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_items_revision_id",
                table: "theory_paper_items",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_items_rule_id",
                table: "theory_paper_items",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_options_item_id_order",
                table: "theory_paper_options",
                columns: new[] { "item_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_paper_options_option_id",
                table: "theory_paper_options",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_papers_participant_id_attempt",
                table: "theory_papers",
                columns: new[] { "participant_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_papers_state_opened_at",
                table: "theory_papers",
                columns: new[] { "state", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_papers_submitted_by_id",
                table: "theory_papers",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_participants_activity_id_team_id",
                table: "theory_participants",
                columns: new[] { "activity_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_participants_activity_id_user_id",
                table: "theory_participants",
                columns: new[] { "activity_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_participants_team_id",
                table: "theory_participants",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_participants_user_id",
                table: "theory_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_classifications_kind_normalized_name",
                table: "theory_question_classifications",
                columns: new[] { "kind", "normalized_name" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_exposures_activity_id",
                table: "theory_question_exposures",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_files_file_id",
                table: "theory_question_files",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_grants_user_id",
                table: "theory_question_grants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_options_revision_id_order",
                table: "theory_question_options",
                columns: new[] { "revision_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_question_revisions_question_id_version",
                table: "theory_question_revisions",
                columns: new[] { "question_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_theory_questions_owner_id_state_updated_at",
                table: "theory_questions",
                columns: new[] { "owner_id", "state", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_theory_questions_source_id",
                table: "theory_questions",
                column: "source_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_theory_activities");

            migrationBuilder.DropTable(
                name: "theory_active_papers");

            migrationBuilder.DropTable(
                name: "theory_activity_operations");

            migrationBuilder.DropTable(
                name: "theory_activity_pauses");

            migrationBuilder.DropTable(
                name: "theory_answer_receipts");

            migrationBuilder.DropTable(
                name: "theory_draw_candidates");

            migrationBuilder.DropTable(
                name: "theory_draw_receipts");

            migrationBuilder.DropTable(
                name: "theory_draw_rule_filters");

            migrationBuilder.DropTable(
                name: "theory_paper_options");

            migrationBuilder.DropTable(
                name: "theory_question_classifications");

            migrationBuilder.DropTable(
                name: "theory_question_exposures");

            migrationBuilder.DropTable(
                name: "theory_question_files");

            migrationBuilder.DropTable(
                name: "theory_question_grants");

            migrationBuilder.DropTable(
                name: "theory_paper_items");

            migrationBuilder.DropTable(
                name: "theory_question_options");

            migrationBuilder.DropTable(
                name: "theory_draw_rules");

            migrationBuilder.DropTable(
                name: "theory_papers");

            migrationBuilder.DropTable(
                name: "theory_question_revisions");

            migrationBuilder.DropTable(
                name: "theory_participants");

            migrationBuilder.DropTable(
                name: "theory_questions");

            migrationBuilder.DropTable(
                name: "theory_activities");

            migrationBuilder.DropTable(
                name: "theory_question_sources");
        }
    }
}

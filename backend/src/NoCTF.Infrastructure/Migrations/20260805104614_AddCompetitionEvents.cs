using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    related_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_status = table.Column<int>(type: "integer", nullable: true),
                    leaderboard_visibility = table.Column<short>(type: "smallint", nullable: true),
                    team_registration_status = table.Column<int>(type: "integer", nullable: true),
                    submission_kind = table.Column<int>(type: "integer", nullable: true),
                    submission_state = table.Column<short>(type: "smallint", nullable: true),
                    scoring_event_kind = table.Column<int>(type: "integer", nullable: true),
                    scoring_result = table.Column<int>(type: "integer", nullable: true),
                    runtime_state = table.Column<short>(type: "smallint", nullable: true),
                    question_status = table.Column<short>(type: "smallint", nullable: true),
                    runtime_generation = table.Column<int>(type: "integer", nullable: true),
                    host_port = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_events_competition_challenge_hints_hint_id",
                        column: x => x.hint_id,
                        principalTable: "competition_challenge_hints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_competition_challenges_competition_chall",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_competition_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "competition_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_users_related_user_id",
                        column: x => x.related_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_actor_user_id",
                table: "competition_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_challenge_id",
                table: "competition_events",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_actor_user_id_occurred_at",
                table: "competition_events",
                columns: new[] { "competition_id", "actor_user_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_competition_challenge_id_",
                table: "competition_events",
                columns: new[] { "competition_id", "competition_challenge_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_kind_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "kind", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_level_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "level", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_runtime_instance_id_occur",
                table: "competition_events",
                columns: new[] { "competition_id", "runtime_instance_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_team_id_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "team_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_hint_id",
                table: "competition_events",
                column: "hint_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_question_id",
                table: "competition_events",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_related_user_id",
                table: "competition_events",
                column: "related_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_runtime_instance_id",
                table: "competition_events",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_submission_id",
                table: "competition_events",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_team_id",
                table: "competition_events",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_events");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asked_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_questions", x => x.id);
                    table.CheckConstraint("ck_competition_questions_revision", "revision >= 0");
                    table.CheckConstraint("ck_competition_questions_subject_scope", "(subject = 0 AND competition_challenge_id IS NOT NULL) OR (subject = 1 AND competition_challenge_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_competition_questions_competition_challenges_competition_ch",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_questions_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_questions_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_questions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_questions_users_asked_by_user_id",
                        column: x => x.asked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_questions_users_published_by_user_id",
                        column: x => x.published_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_question_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    actor_role = table.Column<short>(type: "smallint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    from_status = table.Column<short>(type: "smallint", nullable: true),
                    to_status = table.Column<short>(type: "smallint", nullable: true),
                    target_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_question_entries", x => x.id);
                    table.CheckConstraint("ck_competition_question_entries_shape", "(kind = 0 AND body IS NOT NULL AND from_status IS NULL AND to_status IS NULL) OR (kind = 1 AND body IS NULL AND from_status IS NOT NULL AND to_status IS NOT NULL) OR (kind = 2 AND body IS NULL AND target_entry_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_competition_question_entries_competition_question_entries_t",
                        column: x => x.target_entry_id,
                        principalTable: "competition_question_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_question_entries_competition_questions_question",
                        column: x => x.question_id,
                        principalTable: "competition_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_question_entries_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_question_entries_users_published_by_user_id",
                        column: x => x.published_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_question_entries_actor_user_id",
                table: "competition_question_entries",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_question_entries_published_by_user_id",
                table: "competition_question_entries",
                column: "published_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_question_entries_question_id_created_at_id",
                table: "competition_question_entries",
                columns: new[] { "question_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_question_entries_target_entry_id",
                table: "competition_question_entries",
                column: "target_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_asked_by_user_id",
                table: "competition_questions",
                column: "asked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_competition_challenge_id_status_updat",
                table: "competition_questions",
                columns: new[] { "competition_challenge_id", "status", "updated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_competition_id_published_at_id",
                table: "competition_questions",
                columns: new[] { "competition_id", "published_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_competition_id_updated_at_id",
                table: "competition_questions",
                columns: new[] { "competition_id", "updated_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_published_by_user_id",
                table: "competition_questions",
                column: "published_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_submission_id",
                table: "competition_questions",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_questions_team_id_created_at_id",
                table: "competition_questions",
                columns: new[] { "team_id", "created_at", "id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_question_entries");

            migrationBuilder.DropTable(
                name: "competition_questions");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionChallengeWriteUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "single_write_up_deadline_hours",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 24);

            migrationBuilder.AddColumn<int>(
                name: "single_write_up_deduction_percent",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 20);

            migrationBuilder.AddColumn<bool>(
                name: "single_write_ups_enabled",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "write_up_deduction_percent",
                table: "competition_challenges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "competition_challenge_writeups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    author_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    next_version_number = table.Column<int>(type: "integer", nullable: false),
                    draft_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeups", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_competition_challenges_compe",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_writeup_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    write_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    markdown = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<long>(type: "bigint", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeup_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_competition_challeng",
                        column: x => x.write_up_id,
                        principalTable: "competition_challenge_writeups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_users_reviewed_by_us",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_writeup_unlocks",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deduction_percent = table.Column<int>(type: "integer", nullable: false),
                    unlocked_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeup_unlocks", x => x.gameplay_fact_id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competition_challenge",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competition_challenge1",
                        column: x => x.version_id,
                        principalTable: "competition_challenge_writeup_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competitions_competit",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_gameplay_facts_gamepl",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_competition_challenge",
                table: "competition_challenge_writeup_unlocks",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_competition_id",
                table: "competition_challenge_writeup_unlocks",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_team_id_competition_c",
                table: "competition_challenge_writeup_unlocks",
                columns: new[] { "team_id", "competition_challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_version_id",
                table: "competition_challenge_writeup_unlocks",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_actor_user_id",
                table: "competition_challenge_writeup_versions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_file_id",
                table: "competition_challenge_writeup_versions",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_reviewed_by_user_id",
                table: "competition_challenge_writeup_versions",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_write_up_id_number",
                table: "competition_challenge_writeup_versions",
                columns: new[] { "write_up_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_competition_challenge_id_sou",
                table: "competition_challenge_writeups",
                columns: new[] { "competition_challenge_id", "source", "author_scope_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_competition_id",
                table: "competition_challenge_writeups",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_team_id",
                table: "competition_challenge_writeups",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_challenge_writeup_unlocks");

            migrationBuilder.DropTable(
                name: "competition_challenge_writeup_versions");

            migrationBuilder.DropTable(
                name: "competition_challenge_writeups");

            migrationBuilder.DropColumn(
                name: "single_write_up_deadline_hours",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "single_write_up_deduction_percent",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "single_write_ups_enabled",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "write_up_deduction_percent",
                table: "competition_challenges");
        }
    }
}

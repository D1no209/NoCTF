using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloDirectionsRescueAndMatchDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "direction_draw_id",
                table: "live_solo_rounds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "deleted_at",
                table: "live_solo_matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "deleted_by_user_id",
                table: "live_solo_matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_solo_direction_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_direction_presets", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_direction_presets_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_direction_presets_live_solo_question_groups_quest",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_match_directions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: true),
                    drawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_match_directions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_match_directions_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_match_directions_live_solo_question_groups_questi",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_rescue_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    requested_at = table.Column<long>(type: "bigint", nullable: false),
                    judge_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_rescue_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_judge_user_id",
                        column: x => x.judge_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_round_roster_members",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_round_roster_members", x => new { x.round_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_direction_draw_id",
                table: "live_solo_rounds",
                column: "direction_draw_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_deleted_by_user_id",
                table: "live_solo_matches",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_direction_presets_competition_id",
                table: "live_solo_direction_presets",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_direction_presets_question_group_id",
                table: "live_solo_direction_presets",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_position",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_question_group_id",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "question_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_round_number",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "round_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_question_group_id",
                table: "live_solo_match_directions",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_actor_user_id",
                table: "live_solo_rescue_requests",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_judge_user_id",
                table: "live_solo_rescue_requests",
                column: "judge_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_match_id",
                table: "live_solo_rescue_requests",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_round_id_team_id_state",
                table: "live_solo_rescue_requests",
                columns: new[] { "round_id", "team_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_team_id",
                table: "live_solo_rescue_requests",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_user_id",
                table: "live_solo_rescue_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_roster_members_team_id",
                table: "live_solo_round_roster_members",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_roster_members_user_id",
                table: "live_solo_round_roster_members",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_users_deleted_by_user_id",
                table: "live_solo_matches",
                column: "deleted_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_rounds_live_solo_match_directions_direction_draw_",
                table: "live_solo_rounds",
                column: "direction_draw_id",
                principalTable: "live_solo_match_directions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_users_deleted_by_user_id",
                table: "live_solo_matches");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_live_solo_match_directions_direction_draw_",
                table: "live_solo_rounds");

            migrationBuilder.DropTable(
                name: "live_solo_direction_presets");

            migrationBuilder.DropTable(
                name: "live_solo_match_directions");

            migrationBuilder.DropTable(
                name: "live_solo_rescue_requests");

            migrationBuilder.DropTable(
                name: "live_solo_round_roster_members");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_rounds_direction_draw_id",
                table: "live_solo_rounds");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_deleted_by_user_id",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "direction_draw_id",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "deleted_by_user_id",
                table: "live_solo_matches");
        }
    }
}

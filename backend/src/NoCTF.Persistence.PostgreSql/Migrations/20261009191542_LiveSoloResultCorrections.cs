using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloResultCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position",
                table: "live_solo_matches");

            migrationBuilder.AddColumn<int>(
                name: "bracket_generation",
                table: "live_solo_matches",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "pending_correction_id",
                table: "live_solo_matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_match_id",
                table: "live_solo_matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "superseded_at",
                table: "live_solo_matches",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_solo_result_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    previous_winner_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_left_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_result_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_correction_matches",
                columns: table => new
                {
                    correction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_state = table.Column<short>(type: "smallint", nullable: false),
                    was_started = table.Column<bool>(type: "boolean", nullable: false),
                    replacement_match_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_correction_matches", x => new { x.correction_id, x.match_id });
                    table.ForeignKey(
                        name: "fk_live_solo_correction_matches_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_correction_matches_live_solo_matches_replacement_",
                        column: x => x.replacement_match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_correction_matches_live_solo_result_corrections_c",
                        column: x => x.correction_id,
                        principalTable: "live_solo_result_corrections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position_bracke",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "lane", "stage", "position", "bracket_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_pending_correction_id",
                table: "live_solo_matches",
                column: "pending_correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_replacement_match_id",
                table: "live_solo_matches",
                column: "replacement_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_correction_matches_match_id",
                table: "live_solo_correction_matches",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_correction_matches_replacement_match_id",
                table: "live_solo_correction_matches",
                column: "replacement_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_actor_user_id",
                table: "live_solo_result_corrections",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_competition_id",
                table: "live_solo_result_corrections",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_match_id_created_at",
                table: "live_solo_result_corrections",
                columns: new[] { "match_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_resolved_by_user_id",
                table: "live_solo_result_corrections",
                column: "resolved_by_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_matches_replacement_match_id",
                table: "live_solo_matches",
                column: "replacement_match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_result_correction_pending_corre",
                table: "live_solo_matches",
                column: "pending_correction_id",
                principalTable: "live_solo_result_corrections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_live_solo_matches_replacement_match_id",
                table: "live_solo_matches");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_live_solo_result_correction_pending_corre",
                table: "live_solo_matches");

            migrationBuilder.DropTable(
                name: "live_solo_correction_matches");

            migrationBuilder.DropTable(
                name: "live_solo_result_corrections");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position_bracke",
                table: "live_solo_matches");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_pending_correction_id",
                table: "live_solo_matches");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_replacement_match_id",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "bracket_generation",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "pending_correction_id",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "replacement_match_id",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "superseded_at",
                table: "live_solo_matches");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "lane", "stage", "position" },
                unique: true);
        }
    }
}

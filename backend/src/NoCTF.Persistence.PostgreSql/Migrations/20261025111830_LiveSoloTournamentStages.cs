using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloTournamentStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_live_solo_stage_rules",
                table: "live_solo_stage_rules");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position_bracke",
                table: "live_solo_matches");

            migrationBuilder.AddColumn<int>(
                name: "tournament_stage",
                table: "live_solo_stage_rules",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "swiss_losses",
                table: "live_solo_matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "swiss_wins",
                table: "live_solo_matches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tournament_stage",
                table: "live_solo_matches",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddPrimaryKey(
                name: "pk_live_solo_stage_rules",
                table: "live_solo_stage_rules",
                columns: new[] { "competition_id", "tournament_stage", "lane", "stage" });

            migrationBuilder.CreateTable(
                name: "live_solo_tournament_stages",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    advance_count = table.Column<int>(type: "integer", nullable: false),
                    swiss_advance_wins = table.Column<int>(type: "integer", nullable: false),
                    swiss_eliminate_losses = table.Column<int>(type: "integer", nullable: false),
                    swiss_qualification_required_wins = table.Column<int>(type: "integer", nullable: false),
                    swiss_elimination_required_wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_tournament_stages", x => new { x.competition_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_tournament_stages_competition_mode_configuration_",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_tournament_stage_lane_stag",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "tournament_stage", "lane", "stage", "position", "bracket_generation" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_tournament_stages");

            migrationBuilder.DropPrimaryKey(
                name: "pk_live_solo_stage_rules",
                table: "live_solo_stage_rules");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_competition_id_tournament_stage_lane_stag",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "tournament_stage",
                table: "live_solo_stage_rules");

            migrationBuilder.DropColumn(
                name: "swiss_losses",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "swiss_wins",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "tournament_stage",
                table: "live_solo_matches");

            migrationBuilder.AddPrimaryKey(
                name: "pk_live_solo_stage_rules",
                table: "live_solo_stage_rules",
                columns: new[] { "competition_id", "lane", "stage" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position_bracke",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "lane", "stage", "position", "bracket_generation" },
                unique: true);
        }
    }
}

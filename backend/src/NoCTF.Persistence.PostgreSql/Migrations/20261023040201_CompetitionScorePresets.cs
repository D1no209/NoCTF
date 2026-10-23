using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionScorePresets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "competition_score_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    score_curve_initial_points = table.Column<long>(type: "bigint", nullable: false),
                    score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: false),
                    score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: false),
                    score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: false),
                    score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_score_presets", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_score_presets_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_score_presets_competition_id_normalized_name",
                table: "competition_score_presets",
                columns: new[] { "competition_id", "normalized_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "competition_score_presets");
        }
    }
}

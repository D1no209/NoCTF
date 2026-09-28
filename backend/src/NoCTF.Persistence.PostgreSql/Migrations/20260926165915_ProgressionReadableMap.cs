using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ProgressionReadableMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "position_x",
                table: "competition_progression_nodes");

            migrationBuilder.DropColumn(
                name: "position_y",
                table: "competition_progression_nodes");

            migrationBuilder.CreateTable(
                name: "team_progression_node_visits",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_opened_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_progression_node_visits", x => new { x.team_id, x.node_id });
                    table.ForeignKey(
                        name: "fk_team_progression_node_visits_competition_progression_nodes_",
                        column: x => x.node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_progression_node_visits_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_visits_competition_id_team_id",
                table: "team_progression_node_visits",
                columns: new[] { "competition_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_visits_node_id",
                table: "team_progression_node_visits",
                column: "node_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_progression_node_visits");

            migrationBuilder.AddColumn<double>(
                name: "position_x",
                table: "competition_progression_nodes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "position_y",
                table: "competition_progression_nodes",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}

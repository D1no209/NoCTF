using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdTieBreakPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "awd_competition_tie_breaks",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<short>(type: "smallint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_competition_tie_breaks", x => new { x.competition_id, x.position });
                    table.ForeignKey(
                        name: "fk_awd_competition_tie_breaks_competition_mode_configuration_c",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_tie_breaks",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<short>(type: "smallint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_round_tie_breaks", x => new { x.round_id, x.position });
                    table.ForeignKey(
                        name: "fk_awd_round_tie_breaks_awd_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "awd_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awd_competition_tie_breaks_competition_id_field",
                table: "awd_competition_tie_breaks",
                columns: new[] { "competition_id", "field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_round_tie_breaks_round_id_field",
                table: "awd_round_tie_breaks",
                columns: new[] { "round_id", "field" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "awd_competition_tie_breaks");

            migrationBuilder.DropTable(
                name: "awd_round_tie_breaks");
        }
    }
}

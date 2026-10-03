using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionDirectionCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "direction_id",
                table: "competition_challenges",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "competition_directions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    template_direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_directions", x => x.id);
                    table.UniqueConstraint("ak_competition_direction_competition_id_id", x => new { x.competition_id, x.id });
                    table.ForeignKey(
                        name: "fk_competition_directions_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_direction_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "direction_id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_directions_competition_id_normalized_name",
                table: "competition_directions",
                columns: new[] { "competition_id", "normalized_name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_competition_challenges_competition_direction_competition_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "direction_id" },
                principalTable: "competition_directions",
                principalColumns: new[] { "competition_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_competition_challenges_competition_direction_competition_id",
                table: "competition_challenges");

            migrationBuilder.DropTable(
                name: "competition_directions");

            migrationBuilder.DropIndex(
                name: "ix_competition_challenges_competition_id_direction_id",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "direction_id",
                table: "competition_challenges");
        }
    }
}

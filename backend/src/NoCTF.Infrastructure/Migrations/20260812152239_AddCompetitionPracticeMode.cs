using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionPracticeMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances");

            migrationBuilder.AddColumn<bool>(
                name: "practice_mode_enabled",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "purpose IN (0, 2) AND state IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "practice_mode_enabled",
                table: "competitions");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "purpose = 0 AND state IN (0, 1, 2)");
        }
    }
}

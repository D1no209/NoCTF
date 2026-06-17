using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdScoringConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AttackPoints",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BeenAttackedPenalty",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FlagValidityRounds",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceDownPenalty",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceOnlinePoints",
                table: "Competitions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AttackPoints",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "BeenAttackedPenalty",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "FlagValidityRounds",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "ServiceDownPenalty",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "ServiceOnlinePoints",
                table: "Competitions");
        }
    }
}

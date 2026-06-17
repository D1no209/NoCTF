using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdCheckResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CheckerConfig_Command",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckerConfig_Image",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CheckerConfig_TimeoutSeconds",
                table: "Challenges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwdCheckResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdCheckResults", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awdcheckresults_competition",
                table: "AwdCheckResults",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_awdcheckresults_competition_round",
                table: "AwdCheckResults",
                columns: new[] { "CompetitionId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "ix_awdcheckresults_competition_team_challenge_round",
                table: "AwdCheckResults",
                columns: new[] { "CompetitionId", "TeamId", "ChallengeId", "RoundNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdCheckResults");

            migrationBuilder.DropColumn(
                name: "CheckerConfig_Command",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "CheckerConfig_Image",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "CheckerConfig_TimeoutSeconds",
                table: "Challenges");
        }
    }
}

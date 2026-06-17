using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdFlagEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FlagFormat",
                table: "Competitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlagPath",
                table: "Competitions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwdFlags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    FlagContent = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdFlags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AwdGameBoxes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ContainerInstanceId = table.Column<string>(type: "text", nullable: true),
                    LastFlagRefreshedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdGameBoxes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awdflags_competition",
                table: "AwdFlags",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_awdflags_competition_team_challenge_round",
                table: "AwdFlags",
                columns: new[] { "CompetitionId", "TeamId", "ChallengeId", "RoundNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awdgameboxes_competition",
                table: "AwdGameBoxes",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_awdgameboxes_competition_team_challenge",
                table: "AwdGameBoxes",
                columns: new[] { "CompetitionId", "TeamId", "ChallengeId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdFlags");

            migrationBuilder.DropTable(
                name: "AwdGameBoxes");

            migrationBuilder.DropColumn(
                name: "FlagFormat",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "FlagPath",
                table: "Competitions");
        }
    }
}

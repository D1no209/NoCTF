using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdpPatchSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefensePoints",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckerConfig_ExpCommand",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CheckerConfig_ExpImage",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwdpPatchSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatchArchiveUrl = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidationDetail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdpPatchSubmissions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awdpatchsubmissions_competition",
                table: "AwdpPatchSubmissions",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_awdpatchsubmissions_competition_team_challenge",
                table: "AwdpPatchSubmissions",
                columns: new[] { "CompetitionId", "TeamId", "ChallengeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdpPatchSubmissions");

            migrationBuilder.DropColumn(
                name: "DefensePoints",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "CheckerConfig_ExpCommand",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "CheckerConfig_ExpImage",
                table: "Challenges");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKohControlRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ControlPointsPerInterval",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PollIntervalSeconds",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KohAgentConfig_ApiKey",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "KohAgentConfig_Port",
                table: "Challenges",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "KohControlRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KohControlRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_kohcontrolrecords_competition",
                table: "KohControlRecords",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_kohcontrolrecords_competition_challenge",
                table: "KohControlRecords",
                columns: new[] { "CompetitionId", "ChallengeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KohControlRecords");

            migrationBuilder.DropColumn(
                name: "ControlPointsPerInterval",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "PollIntervalSeconds",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "KohAgentConfig_ApiKey",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "KohAgentConfig_Port",
                table: "Challenges");
        }
    }
}

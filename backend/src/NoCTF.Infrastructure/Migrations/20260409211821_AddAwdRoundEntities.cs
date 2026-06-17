using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdRoundEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoundNumber",
                table: "ScoreEvents",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoundDurationSeconds",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalRounds",
                table: "Competitions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AwdAttackRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttackerTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    VictimTeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    FlagContent = table.Column<string>(type: "text", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdAttackRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AwdRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwdRounds", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awdattackrecords_competition",
                table: "AwdAttackRecords",
                column: "CompetitionId");

            migrationBuilder.CreateIndex(
                name: "ix_awdattackrecords_competition_round",
                table: "AwdAttackRecords",
                columns: new[] { "CompetitionId", "RoundNumber" });

            migrationBuilder.CreateIndex(
                name: "ix_awdrounds_competition",
                table: "AwdRounds",
                column: "CompetitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwdAttackRecords");

            migrationBuilder.DropTable(
                name: "AwdRounds");

            migrationBuilder.DropColumn(
                name: "RoundNumber",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "RoundDurationSeconds",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "TotalRounds",
                table: "Competitions");
        }
    }
}

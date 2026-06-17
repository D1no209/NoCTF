using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddModeScoringAbstractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "ScoreEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "MetadataJson",
                table: "ScoreEvents",
                type: "text",
                nullable: false,
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "ScoringKey",
                table: "ScoreEvents",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceSignalId",
                table: "ScoreEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModeKey",
                table: "Competitions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScoringProfileJson",
                table: "Competitions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "Competitions"
                SET "ModeKey" = CASE "GameModeType"
                    WHEN 0 THEN 'ctf'
                    WHEN 1 THEN 'awd'
                    WHEN 2 THEN 'awdp'
                    WHEN 3 THEN 'koh'
                    ELSE lower("GameModeType"::text)
                END
                WHERE "ModeKey" = '';
                """);

            migrationBuilder.Sql("""
                UPDATE "Competitions"
                SET "ScoringProfileJson" = CASE "GameModeType"
                    WHEN 0 THEN '["decay-solve"]'
                    WHEN 1 THEN '["round-accumulation"]'
                    WHEN 2 THEN '["round-accumulation","one-shot-verification"]'
                    WHEN 3 THEN '["control-interval"]'
                    ELSE '[]'
                END
                WHERE "ScoringProfileJson" = '';
                """);

            migrationBuilder.CreateTable(
                name: "ScoreSignals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectType = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    SignalType = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RoundNumber = table.Column<int>(type: "integer", nullable: true),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScoreSignals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_scoreevents_competition_idempotency",
                table: "ScoreEvents",
                columns: new[] { "CompetitionId", "IdempotencyKey" },
                unique: true,
                filter: "\"IdempotencyKey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_scoresignals_competition_team_type",
                table: "ScoreSignals",
                columns: new[] { "CompetitionId", "TeamId", "SignalType" });

            migrationBuilder.CreateIndex(
                name: "ux_scoresignals_competition_idempotency",
                table: "ScoreSignals",
                columns: new[] { "CompetitionId", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScoreSignals");

            migrationBuilder.DropIndex(
                name: "ux_scoreevents_competition_idempotency",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "MetadataJson",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "ScoringKey",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "SourceSignalId",
                table: "ScoreEvents");

            migrationBuilder.DropColumn(
                name: "ModeKey",
                table: "Competitions");

            migrationBuilder.DropColumn(
                name: "ScoringProfileJson",
                table: "Competitions");
        }
    }
}

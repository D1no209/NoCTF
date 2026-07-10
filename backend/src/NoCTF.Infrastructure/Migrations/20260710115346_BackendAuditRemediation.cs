using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    public partial class BackendAuditRemediation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                "TokenVersion", "Users", "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>("AttachmentStorageKey", "ChallengeTemplates", "text", nullable: true);
            migrationBuilder.AddColumn<string>("PatchTemplateStorageKey", "ChallengeTemplates", "text", nullable: true);
            migrationBuilder.AddColumn<string>("AttachmentStorageKey", "Challenges", "text", nullable: true);
            migrationBuilder.AddColumn<string>("PatchTemplateStorageKey", "Challenges", "text", nullable: true);
            migrationBuilder.AddColumn<string>("LockOwner", "BackgroundTasks", "text", nullable: true);
            migrationBuilder.AddColumn<string>("ComposeProjectName", "AwdGameBoxes", "text", nullable: true);
            migrationBuilder.AddColumn<string>("ComposeYaml", "AwdGameBoxes", "text", nullable: true);
            migrationBuilder.AddColumn<string>("InternalHost", "AwdGameBoxes", "text", nullable: true);
            migrationBuilder.AddColumn<string>(
                "InternalPortMappingsJson", "AwdGameBoxes", "text", nullable: false, defaultValue: "{}");
            migrationBuilder.AddColumn<string>(
                "RuntimeKind", "AwdGameBoxes", "text", nullable: false, defaultValue: "container");

            migrationBuilder.CreateTable(
                name: "CompetitionEngineStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EngineKey = table.Column<string>(type: "text", nullable: false),
                    LastExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionEngineStates", x => x.Id);
                    table.ForeignKey(
                        "FK_CompetitionEngineStates_Competitions_CompetitionId",
                        x => x.CompetitionId,
                        "Competitions",
                        "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                DELETE FROM "TeamMembers" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "ChallengeHints" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "Submissions" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "ScoreEvents" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "ScoreSignals" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "BackgroundTasks" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "Challenges" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "Teams" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                """);

            migrationBuilder.CreateIndex(
                "ix_submissions_rate_limit_window",
                "Submissions",
                new[] { "CompetitionId", "TeamId", "ChallengeId", "SubmittedAt" });
            migrationBuilder.CreateIndex(
                "ux_competitionenginestates_competition_engine",
                "CompetitionEngineStates",
                new[] { "CompetitionId", "EngineKey" },
                unique: true);

            AddCompetitionForeignKey(migrationBuilder, "Teams");
            AddCompetitionForeignKey(migrationBuilder, "TeamMembers");
            AddCompetitionForeignKey(migrationBuilder, "Challenges");
            AddCompetitionForeignKey(migrationBuilder, "ChallengeHints");
            AddCompetitionForeignKey(migrationBuilder, "Submissions");
            AddCompetitionForeignKey(migrationBuilder, "ScoreEvents");
            AddCompetitionForeignKey(migrationBuilder, "ScoreSignals");
            AddCompetitionForeignKey(migrationBuilder, "BackgroundTasks");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            DropCompetitionForeignKey(migrationBuilder, "BackgroundTasks");
            DropCompetitionForeignKey(migrationBuilder, "ScoreSignals");
            DropCompetitionForeignKey(migrationBuilder, "ScoreEvents");
            DropCompetitionForeignKey(migrationBuilder, "Submissions");
            DropCompetitionForeignKey(migrationBuilder, "ChallengeHints");
            DropCompetitionForeignKey(migrationBuilder, "Challenges");
            DropCompetitionForeignKey(migrationBuilder, "TeamMembers");
            DropCompetitionForeignKey(migrationBuilder, "Teams");

            migrationBuilder.DropTable("CompetitionEngineStates");
            migrationBuilder.DropIndex("ix_submissions_rate_limit_window", "Submissions");
            migrationBuilder.DropColumn("AttachmentStorageKey", "ChallengeTemplates");
            migrationBuilder.DropColumn("PatchTemplateStorageKey", "ChallengeTemplates");
            migrationBuilder.DropColumn("AttachmentStorageKey", "Challenges");
            migrationBuilder.DropColumn("PatchTemplateStorageKey", "Challenges");
            migrationBuilder.DropColumn("LockOwner", "BackgroundTasks");
            migrationBuilder.DropColumn("ComposeProjectName", "AwdGameBoxes");
            migrationBuilder.DropColumn("ComposeYaml", "AwdGameBoxes");
            migrationBuilder.DropColumn("InternalHost", "AwdGameBoxes");
            migrationBuilder.DropColumn("InternalPortMappingsJson", "AwdGameBoxes");
            migrationBuilder.DropColumn("RuntimeKind", "AwdGameBoxes");
            migrationBuilder.DropColumn("TokenVersion", "Users");
        }

        private static void AddCompetitionForeignKey(MigrationBuilder migrationBuilder, string table)
            => migrationBuilder.AddForeignKey(
                name: $"FK_{table}_Competitions_CompetitionId",
                table: table,
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

        private static void DropCompetitionForeignKey(MigrationBuilder migrationBuilder, string table)
            => migrationBuilder.DropForeignKey(
                name: $"FK_{table}_Competitions_CompetitionId",
                table: table);
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260701001000_AddChallengeTemplatesAndCompetitionBindings")]
    public partial class AddChallengeTemplatesAndCompetitionBindings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Competitions"
                    ADD COLUMN IF NOT EXISTS "DefaultInitialPoints" integer NOT NULL DEFAULT 500,
                    ADD COLUMN IF NOT EXISTS "DefaultMinimumPoints" integer NOT NULL DEFAULT 100,
                    ADD COLUMN IF NOT EXISTS "DefaultDecayFactor" integer NOT NULL DEFAULT 450,
                    ADD COLUMN IF NOT EXISTS "DefaultDecayFunction" text NOT NULL DEFAULT 'quadratic',
                    ADD COLUMN IF NOT EXISTS "DifficultyCoefficient" double precision NOT NULL DEFAULT 1.0;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "TemplateId" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "DescriptionFormat" text NOT NULL DEFAULT 'markdown',
                    ADD COLUMN IF NOT EXISTS "DifficultyCoefficient" double precision NOT NULL DEFAULT 1.0;

                CREATE TABLE IF NOT EXISTS "ChallengeTemplates" (
                    "Id" uuid NOT NULL,
                    "Title" text NOT NULL,
                    "Description" text NULL,
                    "TypeId" text NOT NULL,
                    "AttachmentUrl" text NULL,
                    "ContainerImage" text NULL,
                    "ContainerMode" integer NOT NULL DEFAULT 0,
                    "ComposeYaml" text NULL,
                    "ComposeProjectName" text NULL,
                    "FlagSecret" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    "CheckerConfig_Command" text NULL,
                    "CheckerConfig_ExpCommand" text NULL,
                    "CheckerConfig_ExpImage" text NULL,
                    "CheckerConfig_Image" text NULL,
                    "CheckerConfig_TimeoutSeconds" integer NULL,
                    "KohAgentConfig_ApiKey" text NULL,
                    "KohAgentConfig_Port" integer NULL,
                    CONSTRAINT "PK_ChallengeTemplates" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "ChallengeHints" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "Content" text NOT NULL,
                    "DisplayOrder" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_ChallengeHints" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "ix_challenge_templates_title"
                    ON "ChallengeTemplates" ("Title");

                CREATE INDEX IF NOT EXISTS "ix_challenges_competition_template"
                    ON "Challenges" ("CompetitionId", "TemplateId");

                CREATE INDEX IF NOT EXISTS "ix_challengehints_competition_challenge_order"
                    ON "ChallengeHints" ("CompetitionId", "ChallengeId", "DisplayOrder");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

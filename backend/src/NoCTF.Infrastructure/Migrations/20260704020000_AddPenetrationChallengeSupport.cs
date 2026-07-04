using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260704020000_AddPenetrationChallengeSupport")]
    public partial class AddPenetrationChallengeSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ChallengeTemplates"
                    ADD COLUMN IF NOT EXISTS "PenetrationConfigJson" text NOT NULL DEFAULT '{}';

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "PenetrationConfigJson" text NOT NULL DEFAULT '{}';

                ALTER TABLE "Submissions"
                    ADD COLUMN IF NOT EXISTS "PenetrationFlagId" uuid NULL;

                DROP INDEX IF EXISTS "ux_submissions_correct_once";

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_submissions_correct_once"
                    ON "Submissions" ("CompetitionId", "TeamId", "ChallengeId")
                    WHERE "IsCorrect" = true AND "PenetrationFlagId" IS NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_submissions_penetration_flag_correct_once"
                    ON "Submissions" ("CompetitionId", "TeamId", "ChallengeId", "PenetrationFlagId")
                    WHERE "IsCorrect" = true AND "PenetrationFlagId" IS NOT NULL;

                CREATE TABLE IF NOT EXISTS "PenetrationTopologyTemplates" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "ChallengeTemplateId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "Description" text NULL,
                    "NetworkConfigJson" text NOT NULL DEFAULT '{}',
                    "EntryConfigJson" text NOT NULL DEFAULT '{}',
                    "HealthcheckConfigJson" text NOT NULL DEFAULT '{}',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "PenetrationNodeTemplates" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TopologyTemplateId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "Role" text NOT NULL,
                    "Image" text NOT NULL,
                    "Command" text NULL,
                    "EntrypointJson" text NOT NULL DEFAULT '[]',
                    "EnvironmentJson" text NOT NULL DEFAULT '{}',
                    "PortsJson" text NOT NULL DEFAULT '[]',
                    "VolumesJson" text NOT NULL DEFAULT '[]',
                    "NetworksJson" text NOT NULL DEFAULT '[]',
                    "DependsOnJson" text NOT NULL DEFAULT '[]',
                    "IsEntry" boolean NOT NULL,
                    "IsInternal" boolean NOT NULL,
                    "ResourceLimitJson" text NOT NULL DEFAULT '{}',
                    "HealthcheckJson" text NOT NULL DEFAULT '{}',
                    "DisplayOrder" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "PenetrationFlagTemplates" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "TopologyTemplateId" uuid NOT NULL,
                    "NodeTemplateId" uuid NULL,
                    "Name" text NOT NULL,
                    "Stage" integer NOT NULL,
                    "ValueSecret" text NULL,
                    "ValueHash" text NULL,
                    "Score" integer NOT NULL,
                    "IsDynamic" boolean NOT NULL,
                    "Visible" boolean NOT NULL,
                    "InjectionType" integer NOT NULL,
                    "InjectionKey" text NULL,
                    "HintAfterSolved" text NULL,
                    "SolvedCount" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "PenetrationTopologies" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "Description" text NULL,
                    "NetworkConfigJson" text NOT NULL DEFAULT '{}',
                    "EntryConfigJson" text NOT NULL DEFAULT '{}',
                    "HealthcheckConfigJson" text NOT NULL DEFAULT '{}',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "PenetrationNodes" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "TopologyId" uuid NOT NULL,
                    "Name" text NOT NULL,
                    "Role" text NOT NULL,
                    "Image" text NOT NULL,
                    "Command" text NULL,
                    "EntrypointJson" text NOT NULL DEFAULT '[]',
                    "EnvironmentJson" text NOT NULL DEFAULT '{}',
                    "PortsJson" text NOT NULL DEFAULT '[]',
                    "VolumesJson" text NOT NULL DEFAULT '[]',
                    "NetworksJson" text NOT NULL DEFAULT '[]',
                    "DependsOnJson" text NOT NULL DEFAULT '[]',
                    "IsEntry" boolean NOT NULL,
                    "IsInternal" boolean NOT NULL,
                    "ResourceLimitJson" text NOT NULL DEFAULT '{}',
                    "HealthcheckJson" text NOT NULL DEFAULT '{}',
                    "DisplayOrder" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "PenetrationFlags" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "TopologyId" uuid NOT NULL,
                    "NodeId" uuid NULL,
                    "Name" text NOT NULL,
                    "Stage" integer NOT NULL,
                    "ValueSecret" text NULL,
                    "ValueHash" text NULL,
                    "Score" integer NOT NULL,
                    "IsDynamic" boolean NOT NULL,
                    "Visible" boolean NOT NULL,
                    "InjectionType" integer NOT NULL,
                    "InjectionKey" text NULL,
                    "HintAfterSolved" text NULL,
                    "SolvedCount" integer NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "TeamChallengeInstances" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "TopologyId" uuid NULL,
                    "Status" integer NOT NULL,
                    "ComposeProjectName" text NULL,
                    "RenderedComposeYaml" text NOT NULL DEFAULT '',
                    "ContainerIdsJson" text NOT NULL DEFAULT '[]',
                    "PortMappingsJson" text NOT NULL DEFAULT '{}',
                    "EntryHost" text NULL,
                    "EntryPort" integer NULL,
                    "EntryUrl" text NULL,
                    "ResetCount" integer NOT NULL,
                    "ExpiresAt" timestamp with time zone NULL,
                    "LastActionAt" timestamp with time zone NULL,
                    "LastError" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL
                );

                CREATE TABLE IF NOT EXISTS "DynamicFlagInstances" (
                    "Id" uuid NOT NULL PRIMARY KEY,
                    "CompetitionId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "FlagId" uuid NOT NULL,
                    "InstanceId" uuid NOT NULL,
                    "ValueSecret" text NOT NULL,
                    "ValueHash" text NOT NULL,
                    "IsActive" boolean NOT NULL,
                    "GeneratedAt" timestamp with time zone NOT NULL,
                    "SolvedAt" timestamp with time zone NULL
                );

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_pentopologytemplates_challenge_template"
                    ON "PenetrationTopologyTemplates" ("ChallengeTemplateId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_pennodetemplates_topology_name"
                    ON "PenetrationNodeTemplates" ("TopologyTemplateId", "Name");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_penflagtemplates_topology_stage"
                    ON "PenetrationFlagTemplates" ("TopologyTemplateId", "Stage");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_pentopologies_competition_challenge"
                    ON "PenetrationTopologies" ("CompetitionId", "ChallengeId");
                CREATE INDEX IF NOT EXISTS "ix_pennodes_competition_topology"
                    ON "PenetrationNodes" ("CompetitionId", "TopologyId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_pennodes_competition_topology_name"
                    ON "PenetrationNodes" ("CompetitionId", "TopologyId", "Name");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_penflags_competition_challenge_stage"
                    ON "PenetrationFlags" ("CompetitionId", "ChallengeId", "Stage");
                CREATE INDEX IF NOT EXISTS "ix_penflags_competition_topology"
                    ON "PenetrationFlags" ("CompetitionId", "TopologyId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_teamchallengeinstances_competition_team_challenge"
                    ON "TeamChallengeInstances" ("CompetitionId", "TeamId", "ChallengeId");
                CREATE INDEX IF NOT EXISTS "ix_teamchallengeinstances_competition_status"
                    ON "TeamChallengeInstances" ("CompetitionId", "Status");
                CREATE INDEX IF NOT EXISTS "ix_dynamicflaginstances_competition_team_flag_active"
                    ON "DynamicFlagInstances" ("CompetitionId", "TeamId", "FlagId", "IsActive");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_dynamicflaginstances_active_team_flag"
                    ON "DynamicFlagInstances" ("CompetitionId", "TeamId", "FlagId")
                    WHERE "IsActive" = true;
                CREATE INDEX IF NOT EXISTS "ix_dynamicflaginstances_competition_challenge_flag"
                    ON "DynamicFlagInstances" ("CompetitionId", "ChallengeId", "FlagId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TABLE IF EXISTS "DynamicFlagInstances";
                DROP TABLE IF EXISTS "TeamChallengeInstances";
                DROP TABLE IF EXISTS "PenetrationFlags";
                DROP TABLE IF EXISTS "PenetrationNodes";
                DROP TABLE IF EXISTS "PenetrationTopologies";
                DROP TABLE IF EXISTS "PenetrationFlagTemplates";
                DROP TABLE IF EXISTS "PenetrationNodeTemplates";
                DROP TABLE IF EXISTS "PenetrationTopologyTemplates";
                DROP INDEX IF EXISTS "ux_submissions_penetration_flag_correct_once";
                DROP INDEX IF EXISTS "ux_submissions_correct_once";
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_submissions_correct_once"
                    ON "Submissions" ("CompetitionId", "TeamId", "ChallengeId")
                    WHERE "IsCorrect" = true;
                ALTER TABLE "Submissions" DROP COLUMN IF EXISTS "PenetrationFlagId";
                ALTER TABLE "Challenges" DROP COLUMN IF EXISTS "PenetrationConfigJson";
                ALTER TABLE "ChallengeTemplates" DROP COLUMN IF EXISTS "PenetrationConfigJson";
                """);
        }
    }
}

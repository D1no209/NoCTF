using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [Migration("20260704120000_AddOrchestrationAndKubernetesSupport")]
    public partial class AddOrchestrationAndKubernetesSupport : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "ChallengeTemplates" ADD COLUMN IF NOT EXISTS "OrchestrationJson" text NOT NULL DEFAULT '{}';""");
            migrationBuilder.Sql("""ALTER TABLE "Challenges" ADD COLUMN IF NOT EXISTS "OrchestrationJson" text NOT NULL DEFAULT '{}';""");
            migrationBuilder.Sql("""ALTER TABLE "PenetrationNodeTemplates" ADD COLUMN IF NOT EXISTS "OrchestrationJson" text NOT NULL DEFAULT '{}';""");
            migrationBuilder.Sql("""ALTER TABLE "PenetrationNodes" ADD COLUMN IF NOT EXISTS "OrchestrationJson" text NOT NULL DEFAULT '{}';""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" ADD COLUMN IF NOT EXISTS "ProviderType" text NOT NULL DEFAULT 'docker';""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" ADD COLUMN IF NOT EXISTS "PublicHost" text;""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" ADD COLUMN IF NOT EXISTS "EntryUrl" text;""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" ADD COLUMN IF NOT EXISTS "OrchestrationNamespace" text;""");

            migrationBuilder.Sql("""
                UPDATE "ChallengeTemplates"
                SET "OrchestrationJson" = jsonb_build_object(
                    'provider', 'Docker',
                    'runtime', CASE WHEN "ContainerMode" = 1 THEN 'Compose' ELSE 'SingleContainer' END,
                    'image', "ContainerImage",
                    'exposedPort', "ExposedPort",
                    'composeYaml', "ComposeYaml",
                    'composeProjectName', "ComposeProjectName",
                    'kubernetes', jsonb_build_object('exposure', CASE WHEN "ExposedPort" IS NULL THEN 'None' ELSE 'NodePort' END)
                )::text
                WHERE "OrchestrationJson" = '{}';
                """);

            migrationBuilder.Sql("""
                UPDATE "Challenges"
                SET "OrchestrationJson" = jsonb_build_object(
                    'provider', 'Docker',
                    'runtime', CASE WHEN "ContainerMode" = 1 THEN 'Compose' ELSE 'SingleContainer' END,
                    'image', "ContainerImage",
                    'exposedPort', "ExposedPort",
                    'composeYaml', "ComposeYaml",
                    'composeProjectName', "ComposeProjectName",
                    'kubernetes', jsonb_build_object('exposure', CASE WHEN "ExposedPort" IS NULL THEN 'None' ELSE 'NodePort' END)
                )::text
                WHERE "OrchestrationJson" = '{}';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "ChallengeTemplates" DROP COLUMN IF EXISTS "OrchestrationJson";""");
            migrationBuilder.Sql("""ALTER TABLE "Challenges" DROP COLUMN IF EXISTS "OrchestrationJson";""");
            migrationBuilder.Sql("""ALTER TABLE "PenetrationNodeTemplates" DROP COLUMN IF EXISTS "OrchestrationJson";""");
            migrationBuilder.Sql("""ALTER TABLE "PenetrationNodes" DROP COLUMN IF EXISTS "OrchestrationJson";""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" DROP COLUMN IF EXISTS "ProviderType";""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" DROP COLUMN IF EXISTS "PublicHost";""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" DROP COLUMN IF EXISTS "EntryUrl";""");
            migrationBuilder.Sql("""ALTER TABLE "AwdGameBoxes" DROP COLUMN IF EXISTS "OrchestrationNamespace";""");
        }
    }
}

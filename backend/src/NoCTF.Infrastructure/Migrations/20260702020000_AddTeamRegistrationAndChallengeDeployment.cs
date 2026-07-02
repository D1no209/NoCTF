using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260702020000_AddTeamRegistrationAndChallengeDeployment")]
    public partial class AddTeamRegistrationAndChallengeDeployment : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Competitions"
                    ADD COLUMN IF NOT EXISTS "TeamRegistrationAutoApprove" boolean NOT NULL DEFAULT true,
                    ADD COLUMN IF NOT EXISTS "MaxTeamMembers" integer NOT NULL DEFAULT 5;

                ALTER TABLE "Teams"
                    ADD COLUMN IF NOT EXISTS "InviteToken" text NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "IsLocked" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "RegistrationStatus" integer NOT NULL DEFAULT 1,
                    ADD COLUMN IF NOT EXISTS "RegisteredAt" timestamp with time zone NOT NULL DEFAULT now(),
                    ADD COLUMN IF NOT EXISTS "ApprovedAt" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "ApprovedById" uuid NULL;

                UPDATE "Teams"
                SET "InviteToken" = md5(random()::text || clock_timestamp()::text || "Id"::text)
                WHERE "InviteToken" = '';

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "DeploymentType" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "ExposedPort" integer NULL;

                ALTER TABLE "ChallengeTemplates"
                    ADD COLUMN IF NOT EXISTS "DeploymentType" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "ExposedPort" integer NULL;

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_teams_invite_token"
                    ON "Teams" ("InviteToken");

                CREATE INDEX IF NOT EXISTS "ix_teams_competition_registration_status"
                    ON "Teams" ("CompetitionId", "RegistrationStatus");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

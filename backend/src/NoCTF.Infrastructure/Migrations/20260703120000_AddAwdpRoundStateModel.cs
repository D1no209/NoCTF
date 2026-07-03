using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260703120000_AddAwdpRoundStateModel")]
    public partial class AddAwdpRoundStateModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Competitions"
                    ADD COLUMN IF NOT EXISTS "AwdpAttackScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpDefenseScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxAttackAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxDefenseAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpAllowAttackAfterBreakSuccess" boolean NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpAllowDefenseAfterFixSuccess" boolean NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpServicePenaltyEnabled" boolean NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpServicePenaltyPerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpViolationPenaltyEnabled" boolean NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpViolationPenalty" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixEntry" text NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixTimeoutSeconds" integer NULL;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "AwdpAttackScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpDefenseScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxAttackAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxDefenseAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixEntry" text NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixTimeoutSeconds" integer NULL;

                ALTER TABLE "ChallengeTemplates"
                    ADD COLUMN IF NOT EXISTS "AwdpAttackScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpDefenseScorePerRound" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxAttackAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpMaxDefenseAttempts" integer NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixEntry" text NULL,
                    ADD COLUMN IF NOT EXISTS "AwdpFixTimeoutSeconds" integer NULL;

                ALTER TABLE "AwdpPatchSubmissions"
                    ADD COLUMN IF NOT EXISTS "FixStatus" integer NOT NULL DEFAULT 1,
                    ADD COLUMN IF NOT EXISTS "AttemptNumber" integer NOT NULL DEFAULT 0,
                    ADD COLUMN IF NOT EXISTS "FileName" text NOT NULL DEFAULT '',
                    ADD COLUMN IF NOT EXISTS "FixEntry" text NOT NULL DEFAULT 'fix.sh';

                CREATE TABLE IF NOT EXISTS "AwdpRounds" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "RoundNumber" integer NOT NULL,
                    "StartTime" timestamp with time zone NOT NULL,
                    "EndTime" timestamp with time zone NULL,
                    "Status" integer NOT NULL,
                    CONSTRAINT "PK_AwdpRounds" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "AwdpTeamChallengeStates" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "InstanceStatus" integer NOT NULL DEFAULT 0,
                    "BreakStatus" integer NOT NULL DEFAULT 0,
                    "FixStatus" integer NOT NULL DEFAULT 0,
                    "ServiceStatus" integer NOT NULL DEFAULT 0,
                    "AttackAttempts" integer NOT NULL DEFAULT 0,
                    "DefenseAttempts" integer NOT NULL DEFAULT 0,
                    "BreakSucceededAt" timestamp with time zone NULL,
                    "FixSucceededAt" timestamp with time zone NULL,
                    "LastBreakSubmittedAt" timestamp with time zone NULL,
                    "DefenseRequestedAt" timestamp with time zone NULL,
                    "LastFixSubmittedAt" timestamp with time zone NULL,
                    "LastValidationDetail" text NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_AwdpTeamChallengeStates" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "AwdpRoundScores" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "RoundNumber" integer NOT NULL,
                    "AttackScoreDelta" integer NOT NULL,
                    "DefenseScoreDelta" integer NOT NULL,
                    "PenaltyDelta" integer NOT NULL,
                    "RoundScoreDelta" integer NOT NULL,
                    "Reason" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_AwdpRoundScores" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "ix_awdprounds_competition"
                    ON "AwdpRounds" ("CompetitionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_awdprounds_competition_round"
                    ON "AwdpRounds" ("CompetitionId", "RoundNumber");

                CREATE INDEX IF NOT EXISTS "ix_awdpteamchallengestates_competition"
                    ON "AwdpTeamChallengeStates" ("CompetitionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_awdpteamchallengestates_competition_team_challenge"
                    ON "AwdpTeamChallengeStates" ("CompetitionId", "TeamId", "ChallengeId");

                CREATE INDEX IF NOT EXISTS "ix_awdproundscores_competition"
                    ON "AwdpRoundScores" ("CompetitionId");
                CREATE UNIQUE INDEX IF NOT EXISTS "ux_awdproundscores_competition_round_team_challenge"
                    ON "AwdpRoundScores" ("CompetitionId", "RoundNumber", "TeamId", "ChallengeId");

                UPDATE "Competitions"
                SET "ScoringProfileJson" = '["awdp-round"]'
                WHERE "GameModeType" = 2 OR lower("ModeKey") = 'awdp';
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

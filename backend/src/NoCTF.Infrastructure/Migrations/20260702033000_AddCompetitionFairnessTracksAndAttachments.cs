using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260702033000_AddCompetitionFairnessTracksAndAttachments")]
    public partial class AddCompetitionFairnessTracksAndAttachments : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "Competitions"
                    ADD COLUMN IF NOT EXISTS "TracksEnabled" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "TrackNamesJson" text NOT NULL DEFAULT '[]';

                ALTER TABLE "Teams"
                    ADD COLUMN IF NOT EXISTS "IsBanned" boolean NOT NULL DEFAULT false,
                    ADD COLUMN IF NOT EXISTS "BannedAt" timestamp with time zone NULL,
                    ADD COLUMN IF NOT EXISTS "BannedById" uuid NULL,
                    ADD COLUMN IF NOT EXISTS "BannedReason" text NULL,
                    ADD COLUMN IF NOT EXISTS "TrackName" text NULL;

                ALTER TABLE "Challenges"
                    ADD COLUMN IF NOT EXISTS "FlagPrefix" text NOT NULL DEFAULT 'flag',
                    ADD COLUMN IF NOT EXISTS "FlagEnvironmentVariable" text NOT NULL DEFAULT 'NOCTF_FLAG_UUID';

                ALTER TABLE "ChallengeTemplates"
                    ADD COLUMN IF NOT EXISTS "FlagEnvironmentVariable" text NOT NULL DEFAULT 'NOCTF_FLAG_UUID';

                CREATE TABLE IF NOT EXISTS "CtfDynamicFlags" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "TeamId" uuid NOT NULL,
                    "ChallengeId" uuid NOT NULL,
                    "FlagUuid" text NOT NULL,
                    "EnvironmentVariable" text NOT NULL DEFAULT 'NOCTF_FLAG_UUID',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "LastSubmittedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_CtfDynamicFlags" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "CompetitionLogs" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "Level" text NOT NULL,
                    "EventType" text NOT NULL,
                    "Message" text NOT NULL,
                    "TeamId" uuid NULL,
                    "UserId" uuid NULL,
                    "ChallengeId" uuid NULL,
                    "MetadataJson" text NOT NULL DEFAULT '{}',
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_CompetitionLogs" PRIMARY KEY ("Id")
                );

                CREATE TABLE IF NOT EXISTS "CheatIncidents" (
                    "Id" uuid NOT NULL,
                    "CompetitionId" uuid NOT NULL,
                    "SuspectTeamId" uuid NOT NULL,
                    "VictimTeamId" uuid NULL,
                    "ChallengeId" uuid NOT NULL,
                    "UserId" uuid NOT NULL,
                    "SubmittedFlag" text NOT NULL,
                    "Reason" text NOT NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "Resolved" boolean NOT NULL DEFAULT false,
                    "ResolvedAt" timestamp with time zone NULL,
                    CONSTRAINT "PK_CheatIncidents" PRIMARY KEY ("Id")
                );

                CREATE INDEX IF NOT EXISTS "ix_teams_competition_banned"
                    ON "Teams" ("CompetitionId", "IsBanned");

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_ctfdynamicflags_competition_team_challenge"
                    ON "CtfDynamicFlags" ("CompetitionId", "TeamId", "ChallengeId");

                CREATE UNIQUE INDEX IF NOT EXISTS "ux_ctfdynamicflags_competition_challenge_uuid"
                    ON "CtfDynamicFlags" ("CompetitionId", "ChallengeId", "FlagUuid");

                CREATE INDEX IF NOT EXISTS "ix_competitionlogs_competition_created"
                    ON "CompetitionLogs" ("CompetitionId", "CreatedAt");

                CREATE INDEX IF NOT EXISTS "ix_competitionlogs_competition_event"
                    ON "CompetitionLogs" ("CompetitionId", "EventType");

                CREATE INDEX IF NOT EXISTS "ix_cheatincidents_competition_created"
                    ON "CheatIncidents" ("CompetitionId", "CreatedAt");

                CREATE INDEX IF NOT EXISTS "ix_cheatincidents_competition_suspect"
                    ON "CheatIncidents" ("CompetitionId", "SuspectTeamId");
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

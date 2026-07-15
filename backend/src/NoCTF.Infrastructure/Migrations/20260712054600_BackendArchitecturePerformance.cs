using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackendArchitecturePerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Do not silently rewrite account identifiers. Truncation can transfer an
            // identity to the wrong account, while suffixing can make existing users
            // unable to log in. Fail before any DDL with aggregate-only diagnostics so
            // operators can resolve legacy data deliberately and rerun the migration.
            migrationBuilder.Sql(
                """
                DO $migration$
                DECLARE
                    overlong_email_count bigint;
                    overlong_username_count bigint;
                    normalized_email_collision_count bigint;
                    normalized_username_collision_count bigint;
                BEGIN
                    SELECT count(*)
                    INTO overlong_email_count
                    FROM "Users"
                    WHERE char_length("Email") > 254
                       OR char_length(lower(btrim("Email"))) > 254;

                    SELECT count(*)
                    INTO overlong_username_count
                    FROM "Users"
                    WHERE char_length("UserName") > 64
                       OR char_length(lower(btrim("UserName"))) > 64;

                    SELECT count(*)
                    INTO normalized_email_collision_count
                    FROM (
                        SELECT lower(btrim("Email"))
                        FROM "Users"
                        GROUP BY lower(btrim("Email"))
                        HAVING count(*) > 1
                    ) AS collisions;

                    SELECT count(*)
                    INTO normalized_username_collision_count
                    FROM (
                        SELECT lower(btrim("UserName"))
                        FROM "Users"
                        GROUP BY lower(btrim("UserName"))
                        HAVING count(*) > 1
                    ) AS collisions;

                    IF overlong_email_count > 0
                       OR overlong_username_count > 0
                       OR normalized_email_collision_count > 0
                       OR normalized_username_collision_count > 0 THEN
                        RAISE EXCEPTION USING
                            ERRCODE = 'check_violation',
                            MESSAGE = 'Cannot apply BackendArchitecturePerformance: legacy Users violate the new identity constraints.',
                            DETAIL = format(
                                'overlong emails=%s, overlong user names=%s, normalized email collision groups=%s, normalized user-name collision groups=%s',
                                overlong_email_count,
                                overlong_username_count,
                                normalized_email_collision_count,
                                normalized_username_collision_count),
                            HINT = 'Resolve identifiers longer than Email=254/UserName=64 characters and duplicate lower(trim(identifier)) values, then rerun the migration.';
                    END IF;
                END
                $migration$;
                """);

            migrationBuilder.DropIndex(
                name: "ix_kohcontrolrecords_competition_challenge",
                table: "KohControlRecords");

            migrationBuilder.DropIndex(
                name: "ix_users_email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "ix_users_username",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedEmail",
                table: "Users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true,
                computedColumnSql: "lower(btrim(\"Email\"))",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUserName",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                computedColumnSql: "lower(btrim(\"UserName\"))",
                stored: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuntimeOperationId",
                table: "TeamChallengeInstances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleting",
                table: "Challenges",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CleanupLockedUntil",
                table: "AwdGameBoxes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RuntimeOperationId",
                table: "AwdGameBoxes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CleanupOwner",
                table: "AwdGameBoxes",
                type: "text",
                nullable: true);

            // Dynamic flags previously stored multiple semicolon-separated
            // digests. Keep the canonical raw-value digest so equality matching
            // can use the active hash index.
            migrationBuilder.Sql(
                """
                UPDATE "DynamicFlagInstances"
                SET "ValueHash" = split_part("ValueHash", ';', 1)
                WHERE position(';' in "ValueHash") > 0;
                """);

            // Legacy data may contain more than one open control window for a
            // hill. Preserve the newest and close older duplicates before the
            // partial unique index is created.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "CompetitionId", "ChallengeId"
                               ORDER BY "StartTime" DESC, "Id" DESC) AS row_number
                    FROM "KohControlRecords"
                    WHERE "EndTime" IS NULL
                )
                UPDATE "KohControlRecords" AS record
                SET "EndTime" = record."StartTime"
                FROM ranked
                WHERE record."Id" = ranked."Id"
                  AND ranked.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_penflags_competition_challenge_value_hash",
                table: "PenetrationFlags",
                columns: new[] { "CompetitionId", "ChallengeId", "ValueHash" },
                filter: "\"ValueHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_kohcontrolrecords_competition_challenge_start",
                table: "KohControlRecords",
                columns: new[] { "CompetitionId", "ChallengeId", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "ux_kohcontrolrecords_active_hill",
                table: "KohControlRecords",
                columns: new[] { "CompetitionId", "ChallengeId" },
                unique: true,
                filter: "\"EndTime\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_dynamicflaginstances_active_challenge_hash",
                table: "DynamicFlagInstances",
                columns: new[] { "CompetitionId", "ChallengeId", "ValueHash" },
                filter: "\"IsActive\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_awdpatchsubmissions_competition_team_submitted",
                table: "AwdpPatchSubmissions",
                columns: new[] { "CompetitionId", "TeamId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_awdgameboxes_expired_instances",
                table: "AwdGameBoxes",
                columns: new[] { "ExpiresAt", "CleanupLockedUntil" },
                filter: "\"ContainerInstanceId\" IS NOT NULL AND \"ExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_awdflags_competition_challenge_content",
                table: "AwdFlags",
                columns: new[] { "CompetitionId", "ChallengeId", "FlagContent" });

            migrationBuilder.CreateIndex(
                name: "ix_awdcheckresults_round_team_challenge_checked",
                table: "AwdCheckResults",
                columns: new[] { "CompetitionId", "RoundNumber", "TeamId", "ChallengeId", "CheckedAt" });

            migrationBuilder.CreateIndex(
                name: "ix_awdattackrecords_round_victim_challenge",
                table: "AwdAttackRecords",
                columns: new[] { "CompetitionId", "RoundNumber", "VictimTeamId", "ChallengeId" });

            migrationBuilder.CreateIndex(
                name: "ix_auditlogs_timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "Users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "ix_users_username",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "ix_penflags_competition_challenge_value_hash",
                table: "PenetrationFlags");

            migrationBuilder.DropIndex(
                name: "ix_kohcontrolrecords_competition_challenge_start",
                table: "KohControlRecords");

            migrationBuilder.DropIndex(
                name: "ux_kohcontrolrecords_active_hill",
                table: "KohControlRecords");

            migrationBuilder.DropIndex(
                name: "ix_dynamicflaginstances_active_challenge_hash",
                table: "DynamicFlagInstances");

            migrationBuilder.DropIndex(
                name: "ix_awdpatchsubmissions_competition_team_submitted",
                table: "AwdpPatchSubmissions");

            migrationBuilder.DropIndex(
                name: "ix_awdgameboxes_expired_instances",
                table: "AwdGameBoxes");

            migrationBuilder.DropIndex(
                name: "ix_awdflags_competition_challenge_content",
                table: "AwdFlags");

            migrationBuilder.DropIndex(
                name: "ix_awdcheckresults_round_team_challenge_checked",
                table: "AwdCheckResults");

            migrationBuilder.DropIndex(
                name: "ix_awdattackrecords_round_victim_challenge",
                table: "AwdAttackRecords");

            migrationBuilder.DropIndex(
                name: "ix_auditlogs_timestamp",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "RuntimeOperationId",
                table: "TeamChallengeInstances");

            migrationBuilder.DropColumn(
                name: "IsDeleting",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "CleanupLockedUntil",
                table: "AwdGameBoxes");

            migrationBuilder.DropColumn(
                name: "RuntimeOperationId",
                table: "AwdGameBoxes");

            migrationBuilder.DropColumn(
                name: "CleanupOwner",
                table: "AwdGameBoxes");

            migrationBuilder.DropColumn(
                name: "NormalizedEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUserName",
                table: "Users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(254)",
                oldMaxLength: 254);

            migrationBuilder.AlterColumn<string>(
                name: "UserName",
                table: "Users",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                table: "Users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kohcontrolrecords_competition_challenge",
                table: "KohControlRecords",
                columns: new[] { "CompetitionId", "ChallengeId" });
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeArchitecturePerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_awdrounds_competition",
                table: "AwdRounds");

            // Older releases did not enforce competition ownership for
            // plugin-owned tables. Remove unreachable orphan rows before the
            // cascade constraints are installed.
            migrationBuilder.Sql(
                """
                DELETE FROM "AwdAttackRecords" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdCheckResults" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdFlags" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdGameBoxes" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdpPatchSubmissions" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdpRounds" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdpRoundScores" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdpTeamChallengeStates" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "AwdRounds" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "CheatIncidents" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "CompetitionCollaborators" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "CompetitionLogs" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "CtfDynamicFlags" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "DynamicFlagInstances" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "KohControlRecords" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "PenetrationFlags" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "PenetrationNodes" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "PenetrationTopologies" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                DELETE FROM "TeamChallengeInstances" x WHERE NOT EXISTS (SELECT 1 FROM "Competitions" c WHERE c."Id" = x."CompetitionId");
                """);

            // Older single-replica engines could race during restart and leave
            // duplicate durable rows for one logical round. Preserve the most
            // advanced row before enforcing the cross-replica invariant.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id",
                           row_number() OVER (
                               PARTITION BY "CompetitionId", "RoundNumber"
                               ORDER BY "Status" DESC,
                                        "EndTime" DESC NULLS LAST,
                                        "StartTime",
                                        "Id") AS row_number
                    FROM "AwdRounds"
                )
                DELETE FROM "AwdRounds" AS round
                USING ranked
                WHERE round."Id" = ranked."Id"
                  AND ranked.row_number > 1;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_teamchallengeinstances_expiry_status",
                table: "TeamChallengeInstances",
                columns: new[] { "ExpiresAt", "Status" },
                filter: "\"ExpiresAt\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teamchallengeinstances_status_activity",
                table: "TeamChallengeInstances",
                columns: new[] { "Status", "UpdatedAt", "LastActionAt" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_correct_challenge_time",
                table: "Submissions",
                columns: new[] { "CompetitionId", "ChallengeId", "SubmittedAt" },
                filter: "\"IsCorrect\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_scoresignals_competition_type_subject_team",
                table: "ScoreSignals",
                columns: new[] { "CompetitionId", "SignalType", "SubjectId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "ix_scoreevents_competition_scoring_challenge_team",
                table: "ScoreEvents",
                columns: new[] { "CompetitionId", "ScoringKey", "ChallengeId", "TeamId" });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_status_start",
                table: "Competitions",
                columns: new[] { "Status", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "ix_backgroundtasks_recovery",
                table: "BackgroundTasks",
                columns: new[] { "Status", "UpdatedAt", "LockedUntil" },
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "ix_backgroundtasks_typed_dispatch",
                table: "BackgroundTasks",
                columns: new[] { "Status", "Type", "LockedUntil", "CreatedAt" },
                filter: "\"Status\" IN (0, 4)");

            migrationBuilder.CreateIndex(
                name: "ux_awdrounds_competition_round",
                table: "AwdRounds",
                columns: new[] { "CompetitionId", "RoundNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdAttackRecords_Competitions_CompetitionId",
                table: "AwdAttackRecords",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdCheckResults_Competitions_CompetitionId",
                table: "AwdCheckResults",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdFlags_Competitions_CompetitionId",
                table: "AwdFlags",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdGameBoxes_Competitions_CompetitionId",
                table: "AwdGameBoxes",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdpPatchSubmissions_Competitions_CompetitionId",
                table: "AwdpPatchSubmissions",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdpRounds_Competitions_CompetitionId",
                table: "AwdpRounds",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdpRoundScores_Competitions_CompetitionId",
                table: "AwdpRoundScores",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdpTeamChallengeStates_Competitions_CompetitionId",
                table: "AwdpTeamChallengeStates",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_AwdRounds_Competitions_CompetitionId",
                table: "AwdRounds",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CheatIncidents_Competitions_CompetitionId",
                table: "CheatIncidents",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CompetitionCollaborators_Competitions_CompetitionId",
                table: "CompetitionCollaborators",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CompetitionLogs_Competitions_CompetitionId",
                table: "CompetitionLogs",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_CtfDynamicFlags_Competitions_CompetitionId",
                table: "CtfDynamicFlags",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_DynamicFlagInstances_Competitions_CompetitionId",
                table: "DynamicFlagInstances",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_KohControlRecords_Competitions_CompetitionId",
                table: "KohControlRecords",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationFlags_Competitions_CompetitionId",
                table: "PenetrationFlags",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationNodes_Competitions_CompetitionId",
                table: "PenetrationNodes",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PenetrationTopologies_Competitions_CompetitionId",
                table: "PenetrationTopologies",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TeamChallengeInstances_Competitions_CompetitionId",
                table: "TeamChallengeInstances",
                column: "CompetitionId",
                principalTable: "Competitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AwdAttackRecords_Competitions_CompetitionId",
                table: "AwdAttackRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdCheckResults_Competitions_CompetitionId",
                table: "AwdCheckResults");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdFlags_Competitions_CompetitionId",
                table: "AwdFlags");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdGameBoxes_Competitions_CompetitionId",
                table: "AwdGameBoxes");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdpPatchSubmissions_Competitions_CompetitionId",
                table: "AwdpPatchSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdpRounds_Competitions_CompetitionId",
                table: "AwdpRounds");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdpRoundScores_Competitions_CompetitionId",
                table: "AwdpRoundScores");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdpTeamChallengeStates_Competitions_CompetitionId",
                table: "AwdpTeamChallengeStates");

            migrationBuilder.DropForeignKey(
                name: "FK_AwdRounds_Competitions_CompetitionId",
                table: "AwdRounds");

            migrationBuilder.DropForeignKey(
                name: "FK_CheatIncidents_Competitions_CompetitionId",
                table: "CheatIncidents");

            migrationBuilder.DropForeignKey(
                name: "FK_CompetitionCollaborators_Competitions_CompetitionId",
                table: "CompetitionCollaborators");

            migrationBuilder.DropForeignKey(
                name: "FK_CompetitionLogs_Competitions_CompetitionId",
                table: "CompetitionLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_CtfDynamicFlags_Competitions_CompetitionId",
                table: "CtfDynamicFlags");

            migrationBuilder.DropForeignKey(
                name: "FK_DynamicFlagInstances_Competitions_CompetitionId",
                table: "DynamicFlagInstances");

            migrationBuilder.DropForeignKey(
                name: "FK_KohControlRecords_Competitions_CompetitionId",
                table: "KohControlRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationFlags_Competitions_CompetitionId",
                table: "PenetrationFlags");

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationNodes_Competitions_CompetitionId",
                table: "PenetrationNodes");

            migrationBuilder.DropForeignKey(
                name: "FK_PenetrationTopologies_Competitions_CompetitionId",
                table: "PenetrationTopologies");

            migrationBuilder.DropForeignKey(
                name: "FK_TeamChallengeInstances_Competitions_CompetitionId",
                table: "TeamChallengeInstances");

            migrationBuilder.DropIndex(
                name: "ix_teamchallengeinstances_expiry_status",
                table: "TeamChallengeInstances");

            migrationBuilder.DropIndex(
                name: "ix_teamchallengeinstances_status_activity",
                table: "TeamChallengeInstances");

            migrationBuilder.DropIndex(
                name: "ix_submissions_correct_challenge_time",
                table: "Submissions");

            migrationBuilder.DropIndex(
                name: "ix_scoresignals_competition_type_subject_team",
                table: "ScoreSignals");

            migrationBuilder.DropIndex(
                name: "ix_scoreevents_competition_scoring_challenge_team",
                table: "ScoreEvents");

            migrationBuilder.DropIndex(
                name: "ix_competitions_status_start",
                table: "Competitions");

            migrationBuilder.DropIndex(
                name: "ix_backgroundtasks_recovery",
                table: "BackgroundTasks");

            migrationBuilder.DropIndex(
                name: "ix_backgroundtasks_typed_dispatch",
                table: "BackgroundTasks");

            migrationBuilder.DropIndex(
                name: "ux_awdrounds_competition_round",
                table: "AwdRounds");

            migrationBuilder.CreateIndex(
                name: "ix_awdrounds_competition",
                table: "AwdRounds",
                column: "CompetitionId");
        }
    }
}

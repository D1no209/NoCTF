using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations;

public partial class AddUniqueAwdAttackRecordIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM "AwdAttackRecords" a
            USING "AwdAttackRecords" b
            WHERE a."Id" > b."Id"
              AND a."CompetitionId" = b."CompetitionId"
              AND a."AttackerTeamId" = b."AttackerTeamId"
              AND a."VictimTeamId" = b."VictimTeamId"
              AND a."ChallengeId" = b."ChallengeId"
              AND a."RoundNumber" = b."RoundNumber";
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX IF NOT EXISTS "ix_awdattackrecords_unique_attack"
            ON "AwdAttackRecords" ("CompetitionId", "AttackerTeamId", "VictimTeamId", "ChallengeId", "RoundNumber");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "ix_awdattackrecords_unique_attack";
            """);
    }
}

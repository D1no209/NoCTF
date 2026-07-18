using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillChallengeDirectionsFromTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Challenges" AS challenge
                SET "Direction" = upper(btrim(template."Direction"))
                FROM "ChallengeTemplates" AS template
                WHERE challenge."TemplateId" = template."Id"
                  AND upper(btrim(challenge."Direction")) = 'UNCATEGORIZED'
                  AND upper(btrim(template."Direction")) IN
                      ('WEB', 'PWN', 'MISC', 'REVERSE', 'MOBILE', 'CRYPTO', 'FORENSICS', 'AI', 'BLOCKCHAIN', 'HARDWARE', 'OSINT', 'CLOUD');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}

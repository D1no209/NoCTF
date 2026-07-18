using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChallengeDirections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "ChallengeTemplates",
                type: "text",
                nullable: false,
                defaultValue: "Uncategorized");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "Challenges",
                type: "text",
                nullable: false,
                defaultValue: "Uncategorized");

            migrationBuilder.Sql(
                """
                UPDATE "ChallengeTemplates"
                SET "Direction" = upper(btrim("TypeId"))
                WHERE upper(btrim("TypeId")) IN
                    ('WEB', 'PWN', 'MISC', 'REVERSE', 'MOBILE', 'CRYPTO', 'FORENSICS', 'AI', 'BLOCKCHAIN', 'HARDWARE', 'OSINT', 'CLOUD');

                UPDATE "Challenges"
                SET "Direction" = upper(btrim("TypeId"))
                WHERE upper(btrim("TypeId")) IN
                    ('WEB', 'PWN', 'MISC', 'REVERSE', 'MOBILE', 'CRYPTO', 'FORENSICS', 'AI', 'BLOCKCHAIN', 'HARDWARE', 'OSINT', 'CLOUD');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Direction",
                table: "ChallengeTemplates");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "Challenges");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionWebhooks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "webhook_configuration",
                table: "competitions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{\"schemaVersion\":1,\"targets\":[]}'::jsonb");

            migrationBuilder.AddCheckConstraint(
                name: "ck_competitions_webhook_configuration",
                table: "competitions",
                sql: "jsonb_typeof(webhook_configuration) = 'object' AND (webhook_configuration ->> 'schemaVersion')::integer = 1 AND jsonb_typeof(webhook_configuration -> 'targets') = 'array'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_competitions_webhook_configuration",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "webhook_configuration",
                table: "competitions");
        }
    }
}

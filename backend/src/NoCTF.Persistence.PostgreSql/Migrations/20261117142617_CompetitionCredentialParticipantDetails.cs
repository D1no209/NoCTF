using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class CompetitionCredentialParticipantDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "full_name",
                table: "competition_credentials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "school",
                table: "competition_credentials",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "student_number",
                table: "competition_credentials",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "full_name",
                table: "competition_credentials");

            migrationBuilder.DropColumn(
                name: "school",
                table: "competition_credentials");

            migrationBuilder.DropColumn(
                name: "student_number",
                table: "competition_credentials");
        }
    }
}

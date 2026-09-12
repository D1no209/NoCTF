using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureTeamWriteUpSubmission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "write_up_submission_deadline_hours",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "write_up_submission_required",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "ck_competitions_write_up_submission_deadline_hours",
                table: "competitions",
                sql: "write_up_submission_deadline_hours BETWEEN 0 AND 8760");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_competitions_write_up_submission_deadline_hours",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "write_up_submission_deadline_hours",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "write_up_submission_required",
                table: "competitions");
        }
    }
}

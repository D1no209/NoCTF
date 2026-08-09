using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureCompetitionQuestions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "allow_challenge_owners_to_handle_questions",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "max_active_questions_per_team",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "max_participant_messages_before_handler_reply",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddCheckConstraint(
                name: "ck_competitions_question_limits",
                table: "competitions",
                sql: "max_active_questions_per_team > 0 AND max_participant_messages_before_handler_reply > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_competitions_question_limits",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "allow_challenge_owners_to_handle_questions",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "max_active_questions_per_team",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "max_participant_messages_before_handler_reply",
                table: "competitions");
        }
    }
}

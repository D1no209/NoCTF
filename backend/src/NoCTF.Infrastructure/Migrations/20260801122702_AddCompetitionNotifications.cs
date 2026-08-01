using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_event_key",
                table: "notifications",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "publication_revision",
                table: "competition_challenge_hints",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_source_event_key",
                table: "notifications",
                columns: new[] { "user_id", "source_event_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_user_id_source_event_key",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "source_event_key",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "publication_revision",
                table: "competition_challenge_hints");
        }
    }
}

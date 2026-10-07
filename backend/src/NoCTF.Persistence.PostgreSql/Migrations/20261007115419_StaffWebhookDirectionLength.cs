using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class StaffWebhookDirectionLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "summary_direction",
                table: "competition_staff_webhook_work_items",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "summary_direction",
                table: "competition_staff_webhook_event_items",
                type: "character varying(96)",
                maxLength: 96,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "summary_direction",
                table: "competition_staff_webhook_work_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "summary_direction",
                table: "competition_staff_webhook_event_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96,
                oldNullable: true);
        }
    }
}

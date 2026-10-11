using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloDisclosedDirectionLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "direction",
                table: "live_solo_opened_contents",
                type: "character varying(96)",
                maxLength: 96,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "direction",
                table: "live_solo_opened_contents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(96)",
                oldMaxLength: 96);
        }
    }
}

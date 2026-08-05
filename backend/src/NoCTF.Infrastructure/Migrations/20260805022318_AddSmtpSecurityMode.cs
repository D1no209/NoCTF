using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSmtpSecurityMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "smtp_security_mode",
                table: "email_verification_settings",
                type: "integer",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "email_verification_settings",
                keyColumn: "id",
                keyValue: (short)1,
                column: "smtp_security_mode",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "smtp_security_mode",
                table: "email_verification_settings");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationAdminSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailVerificationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PublicBaseUrl = table.Column<string>(type: "text", nullable: false),
                    TokenLifetimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    ResendCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    SmtpHost = table.Column<string>(type: "text", nullable: false),
                    SmtpPort = table.Column<int>(type: "integer", nullable: false),
                    SmtpEnableSsl = table.Column<bool>(type: "boolean", nullable: false),
                    SmtpUserName = table.Column<string>(type: "text", nullable: false),
                    SmtpPasswordProtected = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SmtpFromAddress = table.Column<string>(type: "text", nullable: false),
                    SmtpFromName = table.Column<string>(type: "text", nullable: false),
                    SmtpTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailVerificationSettings");
        }
    }
}

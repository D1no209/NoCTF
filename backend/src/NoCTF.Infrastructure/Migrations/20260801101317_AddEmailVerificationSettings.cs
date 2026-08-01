using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_verification_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    public_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    token_lifetime_minutes = table.Column<int>(type: "integer", nullable: false),
                    resend_cooldown_seconds = table.Column<int>(type: "integer", nullable: false),
                    smtp_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    smtp_port = table.Column<int>(type: "integer", nullable: false),
                    smtp_enable_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    smtp_user_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    smtp_password_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 2048, nullable: true),
                    smtp_from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    smtp_from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    smtp_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_verification_settings", x => x.id);
                });

            migrationBuilder.InsertData(
                table: "email_verification_settings",
                columns: new[] { "id", "enabled", "public_base_url", "resend_cooldown_seconds", "revision", "smtp_enable_ssl", "smtp_from_address", "smtp_from_name", "smtp_host", "smtp_password_ciphertext", "smtp_port", "smtp_timeout_seconds", "smtp_user_name", "token_lifetime_minutes", "updated_at" },
                values: new object[] { (short)1, false, "https://noctf.local", 60, 1L, true, "", "NoCTF", "", null, 587, 10, "", 1440, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verification_settings");
        }
    }
}

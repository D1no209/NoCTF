using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddManagedHumanVerificationProviders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "human_verification_cap_secret_ciphertext",
                table: "platform_settings",
                type: "bytea",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "human_verification_cap_server_url",
                table: "platform_settings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "human_verification_cap_site_key",
                table: "platform_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<short>(
                name: "human_verification_provider",
                table: "platform_settings",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "human_verification_turnstile_allowed_hostnames",
                table: "platform_settings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<byte[]>(
                name: "human_verification_turnstile_secret_ciphertext",
                table: "platform_settings",
                type: "bytea",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "human_verification_turnstile_site_key",
                table: "platform_settings",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "platform_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "human_verification_cap_secret_ciphertext", "human_verification_cap_server_url", "human_verification_cap_site_key", "human_verification_provider", "human_verification_turnstile_allowed_hostnames", "human_verification_turnstile_secret_ciphertext", "human_verification_turnstile_site_key" },
                values: new object[] { null, "", "", null, new string[0], null, "" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "human_verification_cap_secret_ciphertext",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_cap_server_url",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_cap_site_key",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_provider",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_turnstile_allowed_hostnames",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_turnstile_secret_ciphertext",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "human_verification_turnstile_site_key",
                table: "platform_settings");
        }
    }
}

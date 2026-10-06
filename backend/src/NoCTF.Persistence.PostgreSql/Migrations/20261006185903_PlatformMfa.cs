using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class PlatformMfa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mfa_required",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "mfa_authentication_max_age_seconds",
                table: "sso_providers",
                type: "integer",
                nullable: true,
                defaultValue: 300);

            migrationBuilder.AddColumn<bool>(
                name: "mfa_trust_enabled",
                table: "sso_providers",
                type: "boolean",
                nullable: true,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "mfa_trust_policy_id",
                table: "sso_providers",
                type: "uuid",
                nullable: true,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<short>(
                name: "mfa_policy",
                table: "platform_settings",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "mfa_policy_stamp",
                table: "platform_settings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "mfa_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    operation = table.Column<short>(type: "smallint", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    browser_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    policy_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: true),
                    primary_method = table.Column<short>(type: "smallint", nullable: false),
                    primary_authenticated_at = table.Column<long>(type: "bigint", nullable: false),
                    primary_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recovery_grant_sha256 = table.Column<byte[]>(type: "bytea", nullable: true),
                    recovery_grant_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: true),
                    mail_state = table.Column<short>(type: "smallint", nullable: false),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_acr_values",
                columns: table => new
                {
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_acr_values", x => new { x.sso_provider_id, x.position });
                    table.ForeignKey(
                        name: "fk_oidc_mfa_acr_values_sso_provider_configuration_sso_provider",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_amr_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_amr_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_oidc_mfa_amr_groups_sso_provider_configuration_sso_provider",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    consumed_at = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_mfa_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_totp_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    enabled_at = table.Column<long>(type: "bigint", nullable: false),
                    last_accepted_step = table.Column<long>(type: "bigint", nullable: false),
                    recovery_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_totp_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_totp_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_amr_values",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_amr_values", x => new { x.group_id, x.position });
                    table.ForeignKey(
                        name: "fk_oidc_mfa_amr_values_oidc_mfa_amr_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "oidc_mfa_amr_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "platform_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "mfa_policy", "mfa_policy_stamp" },
                values: new object[] { (short)0, new Guid("00000000-0000-0000-0000-000000000002") });

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_expires_at",
                table: "mfa_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_user_id_purpose",
                table: "mfa_challenges",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_oidc_mfa_amr_groups_sso_provider_id_position",
                table: "oidc_mfa_amr_groups",
                columns: new[] { "sso_provider_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_recovery_codes_user_id_batch_id_code_sha256",
                table: "user_mfa_recovery_codes",
                columns: new[] { "user_id", "batch_id", "code_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_totp_credentials_user_id",
                table: "user_totp_credentials",
                column: "user_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mfa_challenges");

            migrationBuilder.DropTable(
                name: "oidc_mfa_acr_values");

            migrationBuilder.DropTable(
                name: "oidc_mfa_amr_values");

            migrationBuilder.DropTable(
                name: "user_mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "user_totp_credentials");

            migrationBuilder.DropTable(
                name: "oidc_mfa_amr_groups");

            migrationBuilder.DropColumn(
                name: "mfa_required",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_authentication_max_age_seconds",
                table: "sso_providers");

            migrationBuilder.DropColumn(
                name: "mfa_trust_enabled",
                table: "sso_providers");

            migrationBuilder.DropColumn(
                name: "mfa_trust_policy_id",
                table: "sso_providers");

            migrationBuilder.DropColumn(
                name: "mfa_policy",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "mfa_policy_stamp",
                table: "platform_settings");
        }
    }
}

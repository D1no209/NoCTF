using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class PlatformPasskeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "passkey_ceremonies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_version = table.Column<int>(type: "integer", nullable: true),
                    mfa_policy_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    browser_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    configuration_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    protected_protocol_state = table.Column<byte[]>(type: "bytea", maxLength: 16384, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passkey_ceremonies", x => x.id);
                    table.ForeignKey(
                        name: "fk_passkey_ceremonies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1023, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    relying_party_id = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    is_user_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    is_backed_up = table.Column<bool>(type: "boolean", nullable: false),
                    attestation_object = table.Column<byte[]>(type: "bytea", maxLength: 16384, nullable: false),
                    client_data_json = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    last_used_at = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkeys", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_passkey_transports",
                columns: table => new
                {
                    user_passkey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    transport = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkey_transports", x => new { x.user_passkey_id, x.position });
                    table.ForeignKey(
                        name: "fk_user_passkey_transports_user_passkeys_user_passkey_id",
                        column: x => x.user_passkey_id,
                        principalTable: "user_passkeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_passkey_ceremonies_expires_at",
                table: "passkey_ceremonies",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_passkey_ceremonies_user_id_purpose",
                table: "passkey_ceremonies",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_credential_id",
                table: "user_passkeys",
                column: "credential_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_user_id_relying_party_id",
                table: "user_passkeys",
                columns: new[] { "user_id", "relying_party_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "passkey_ceremonies");

            migrationBuilder.DropTable(
                name: "user_passkey_transports");

            migrationBuilder.DropTable(
                name: "user_passkeys");
        }
    }
}

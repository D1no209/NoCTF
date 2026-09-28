using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class PersistedSsoFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sso_flows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protocol = table.Column<short>(type: "smallint", nullable: false),
                    intent = table.Column<short>(type: "smallint", nullable: false),
                    browser_id_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    correlation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_fingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_version = table.Column<int>(type: "integer", nullable: true),
                    nonce = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    pkce_verifier = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    service_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    processing_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    external_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_protocol = table.Column<short>(type: "smallint", nullable: true),
                    external_namespace = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    external_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    failure_code = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_flows", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sso_flows_correlation_hash",
                table: "sso_flows",
                column: "correlation_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sso_flows_expires_at",
                table: "sso_flows",
                column: "expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sso_flows");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloMediaAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "current_media_session_id",
                table: "live_solo_matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "live_solo_media_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    identity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    authentication_method = table.Column<short>(type: "smallint", nullable: false),
                    authenticated_at = table.Column<long>(type: "bigint", nullable: false),
                    mfa_source = table.Column<short>(type: "smallint", nullable: false),
                    mfa_authenticated_at = table.Column<long>(type: "bigint", nullable: true),
                    mfa_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trust_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_media_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_media_grants_live_solo_media_sessions_media_sessi",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_media_grants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_current_media_session_id",
                table: "live_solo_matches",
                column: "current_media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_grants_media_session_id_user_id_role",
                table: "live_solo_media_grants",
                columns: new[] { "media_session_id", "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_grants_user_id",
                table: "live_solo_media_grants",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_media_sessions_current_media_se",
                table: "live_solo_matches",
                column: "current_media_session_id",
                principalTable: "live_solo_media_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_live_solo_media_sessions_current_media_se",
                table: "live_solo_matches");

            migrationBuilder.DropTable(
                name: "live_solo_media_grants");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_matches_current_media_session_id",
                table: "live_solo_matches");

            migrationBuilder.DropColumn(
                name: "current_media_session_id",
                table: "live_solo_matches");
        }
    }
}

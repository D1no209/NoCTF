using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloProgramRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_solo_program_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_state = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_live_solo_media_sessions_media_",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_live_solo_program_captures_prog",
                        column: x => x.program_capture_id,
                        principalTable: "live_solo_program_captures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_actor_user_id",
                table: "live_solo_program_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_media_session_id",
                table: "live_solo_program_decisions",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_program_capture_id_occurred_at",
                table: "live_solo_program_decisions",
                columns: new[] { "program_capture_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_program_decisions");
        }
    }
}

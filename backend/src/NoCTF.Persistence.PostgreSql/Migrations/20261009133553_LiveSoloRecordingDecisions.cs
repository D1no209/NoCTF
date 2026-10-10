using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloRecordingDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "live_solo_recording_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_hold = table.Column<bool>(type: "boolean", nullable: false),
                    hold = table.Column<bool>(type: "boolean", nullable: false),
                    previous_published = table.Column<bool>(type: "boolean", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_recording_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_recording_decisions_live_solo_media_sessions_medi",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_recording_decisions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_actor_user_id",
                table: "live_solo_recording_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_media_session_id",
                table: "live_solo_recording_decisions",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_recording_id_occurred_at",
                table: "live_solo_recording_decisions",
                columns: new[] { "recording_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "live_solo_recording_decisions");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloRecordingReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_id",
                table: "live_solo_recordings");

            migrationBuilder.AddColumn<int>(
                name: "chunk",
                table: "live_solo_recordings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "raw_removed_at",
                table: "live_solo_recordings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "reserved_bytes",
                table: "live_solo_recordings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_i",
                table: "live_solo_recordings",
                columns: new[] { "media_session_id", "user_id", "video_track_id", "chunk" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_i",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "chunk",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "raw_removed_at",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "reserved_bytes",
                table: "live_solo_recordings");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_id",
                table: "live_solo_recordings",
                columns: new[] { "media_session_id", "user_id", "video_track_id" },
                unique: true);
        }
    }
}

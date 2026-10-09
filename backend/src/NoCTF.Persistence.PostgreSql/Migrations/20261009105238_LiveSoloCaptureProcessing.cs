using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloCaptureProcessing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_recordings_media_session_id",
                table: "live_solo_recordings");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_segments_media_session_id_sequence",
                table: "live_solo_program_segments");

            migrationBuilder.AddColumn<long>(
                name: "requested_at",
                table: "live_solo_recordings",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "video_track_id",
                table: "live_solo_recordings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "frame_id",
                table: "live_solo_program_segments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "program_capture_id",
                table: "live_solo_program_segments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "current_program_capture_id",
                table: "live_solo_media_sessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recording_retention_days",
                table: "live_solo_media_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "live_solo_program_captures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    egress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<long>(type: "bigint", nullable: true),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<long>(type: "bigint", nullable: true),
                    imported_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_captures", x => x.id);
                    table.UniqueConstraint("ak_live_solo_program_captures_id_media_session_id", x => new { x.id, x.media_session_id });
                    table.ForeignKey(
                        name: "fk_live_solo_program_captures_live_solo_media_sessions_media_s",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_program_frames",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    match_state = table.Column<short>(type: "smallint", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    right_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    left_team_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    right_team_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    round_number = table.Column<int>(type: "integer", nullable: true),
                    round_state = table.Column<short>(type: "smallint", nullable: true),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: true),
                    active_elapsed_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    limit_seconds = table.Column<int>(type: "integer", nullable: true),
                    paused = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_frames", x => x.id);
                    table.UniqueConstraint("ak_live_solo_program_frames_id_media_session_id", x => new { x.id, x.media_session_id });
                    table.ForeignKey(
                        name: "fk_live_solo_program_frames_live_solo_media_sessions_media_ses",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_program_frame_questions",
                columns: table => new
                {
                    frame_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    opened_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_frame_questions", x => new { x.frame_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_program_frame_questions_live_solo_program_frames_",
                        column: x => x.frame_id,
                        principalTable: "live_solo_program_frames",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_program_frame_questions_live_solo_round_questions",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_id",
                table: "live_solo_recordings",
                columns: new[] { "media_session_id", "user_id", "video_track_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_frame_id_media_session_id",
                table: "live_solo_program_segments",
                columns: new[] { "frame_id", "media_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_media_session_id",
                table: "live_solo_program_segments",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_program_capture_id_media_session",
                table: "live_solo_program_segments",
                columns: new[] { "program_capture_id", "media_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_program_capture_id_sequence",
                table: "live_solo_program_segments",
                columns: new[] { "program_capture_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_sessions_current_program_capture_id_id",
                table: "live_solo_media_sessions",
                columns: new[] { "current_program_capture_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_captures_media_session_id",
                table: "live_solo_program_captures",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_captures_state_requested_at",
                table: "live_solo_program_captures",
                columns: new[] { "state", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frame_questions_round_question_id",
                table: "live_solo_program_frame_questions",
                column: "round_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_occurred_at",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "occurred_at" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_media_sessions_live_solo_program_captures_current",
                table: "live_solo_media_sessions",
                columns: new[] { "current_program_capture_id", "id" },
                principalTable: "live_solo_program_captures",
                principalColumns: new[] { "id", "media_session_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_program_segments_live_solo_program_captures_progr",
                table: "live_solo_program_segments",
                columns: new[] { "program_capture_id", "media_session_id" },
                principalTable: "live_solo_program_captures",
                principalColumns: new[] { "id", "media_session_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_program_segments_live_solo_program_frames_frame_i",
                table: "live_solo_program_segments",
                columns: new[] { "frame_id", "media_session_id" },
                principalTable: "live_solo_program_frames",
                principalColumns: new[] { "id", "media_session_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_media_sessions_live_solo_program_captures_current",
                table: "live_solo_media_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_program_segments_live_solo_program_captures_progr",
                table: "live_solo_program_segments");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_program_segments_live_solo_program_frames_frame_i",
                table: "live_solo_program_segments");

            migrationBuilder.DropTable(
                name: "live_solo_program_captures");

            migrationBuilder.DropTable(
                name: "live_solo_program_frame_questions");

            migrationBuilder.DropTable(
                name: "live_solo_program_frames");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_id",
                table: "live_solo_recordings");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_segments_frame_id_media_session_id",
                table: "live_solo_program_segments");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_segments_media_session_id",
                table: "live_solo_program_segments");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_segments_program_capture_id_media_session",
                table: "live_solo_program_segments");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_segments_program_capture_id_sequence",
                table: "live_solo_program_segments");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_media_sessions_current_program_capture_id_id",
                table: "live_solo_media_sessions");

            migrationBuilder.DropColumn(
                name: "requested_at",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_track_id",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "frame_id",
                table: "live_solo_program_segments");

            migrationBuilder.DropColumn(
                name: "program_capture_id",
                table: "live_solo_program_segments");

            migrationBuilder.DropColumn(
                name: "current_program_capture_id",
                table: "live_solo_media_sessions");

            migrationBuilder.DropColumn(
                name: "recording_retention_days",
                table: "live_solo_media_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id",
                table: "live_solo_recordings",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_media_session_id_sequence",
                table: "live_solo_program_segments",
                columns: new[] { "media_session_id", "sequence" },
                unique: true);
        }
    }
}

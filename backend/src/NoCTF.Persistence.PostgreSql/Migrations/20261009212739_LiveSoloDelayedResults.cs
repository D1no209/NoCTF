using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloDelayedResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_frames_media_session_id_occurred_at",
                table: "live_solo_program_frames");

            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "live_solo_program_frames",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<Guid>(
                name: "match_revision",
                table: "live_solo_program_frames",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "public_at",
                table: "live_solo_program_frames",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "winner_team_id",
                table: "live_solo_program_frames",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_match_revisi",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "kind", "match_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_occurred_at",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "kind", "occurred_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_match_revisi",
                table: "live_solo_program_frames");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_occurred_at",
                table: "live_solo_program_frames");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "live_solo_program_frames");

            migrationBuilder.DropColumn(
                name: "match_revision",
                table: "live_solo_program_frames");

            migrationBuilder.DropColumn(
                name: "public_at",
                table: "live_solo_program_frames");

            migrationBuilder.DropColumn(
                name: "winner_team_id",
                table: "live_solo_program_frames");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_occurred_at",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "occurred_at" },
                unique: true);
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloRecordingRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "failure",
                table: "live_solo_recordings",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "previous_state",
                table: "live_solo_recording_decisions",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "replacement_recording_id",
                table: "live_solo_recording_decisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "state",
                table: "live_solo_recording_decisions",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "failure",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "previous_state",
                table: "live_solo_recording_decisions");

            migrationBuilder.DropColumn(
                name: "replacement_recording_id",
                table: "live_solo_recording_decisions");

            migrationBuilder.DropColumn(
                name: "state",
                table: "live_solo_recording_decisions");
        }
    }
}

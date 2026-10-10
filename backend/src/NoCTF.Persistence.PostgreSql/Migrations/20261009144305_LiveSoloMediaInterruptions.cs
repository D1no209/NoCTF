using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloMediaInterruptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "live_solo_match_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "live_solo_media_alert_kind",
                table: "notifications",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "live_solo_media_session_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "live_solo_screen_state",
                table: "notifications",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "live_solo_match_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "live_solo_media_alert_kind",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "live_solo_media_session_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "live_solo_screen_state",
                table: "notifications");
        }
    }
}

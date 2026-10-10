using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloPlatformVideoPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "live_solo_video_maximum_bitrate_bits_per_second",
                table: "platform_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1000000);

            migrationBuilder.AddColumn<int>(
                name: "live_solo_video_maximum_frames_per_second",
                table: "platform_settings",
                type: "integer",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "live_solo_video_maximum_height",
                table: "platform_settings",
                type: "integer",
                nullable: false,
                defaultValue: 720);

            migrationBuilder.AddColumn<int>(
                name: "live_solo_video_maximum_width",
                table: "platform_settings",
                type: "integer",
                nullable: false,
                defaultValue: 1280);

            migrationBuilder.AddColumn<Guid>(
                name: "live_solo_video_policy_stamp",
                table: "platform_settings",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000005"));

            migrationBuilder.AddColumn<int>(
                name: "video_bitrate_bits_per_second",
                table: "live_solo_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_frames_per_second",
                table: "live_solo_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_height",
                table: "live_solo_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_width",
                table: "live_solo_recordings",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "video_policy_stamp",
                table: "live_solo_recordings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_bitrate_bits_per_second",
                table: "live_solo_program_captures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_frames_per_second",
                table: "live_solo_program_captures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_height",
                table: "live_solo_program_captures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "video_maximum_width",
                table: "live_solo_program_captures",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "video_policy_stamp",
                table: "live_solo_program_captures",
                type: "uuid",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "platform_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "live_solo_video_maximum_bitrate_bits_per_second", "live_solo_video_maximum_frames_per_second", "live_solo_video_maximum_height", "live_solo_video_maximum_width", "live_solo_video_policy_stamp" },
                values: new object[] { 1000000, 10, 720, 1280, new Guid("00000000-0000-0000-0000-000000000005") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "live_solo_video_maximum_bitrate_bits_per_second",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "live_solo_video_maximum_frames_per_second",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "live_solo_video_maximum_height",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "live_solo_video_maximum_width",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "live_solo_video_policy_stamp",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "video_bitrate_bits_per_second",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_maximum_frames_per_second",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_maximum_height",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_maximum_width",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_policy_stamp",
                table: "live_solo_recordings");

            migrationBuilder.DropColumn(
                name: "video_bitrate_bits_per_second",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "video_maximum_frames_per_second",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "video_maximum_height",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "video_maximum_width",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "video_policy_stamp",
                table: "live_solo_program_captures");
        }
    }
}

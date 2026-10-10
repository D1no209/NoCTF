using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloOptionalPlatformStreaming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "platform_streaming_enabled",
                table: "live_solo_rounds",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "platform_streaming_enabled",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "platform_streaming_enabled",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "platform_streaming_enabled",
                table: "competition_mode_configurations");
        }
    }
}

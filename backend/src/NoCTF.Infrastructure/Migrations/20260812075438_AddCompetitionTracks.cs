using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCompetitionTracks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "track_key",
                table: "teams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.AddColumn<string>(
                name: "track_configuration_json",
                table: "competitions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "track_configuration_revision",
                table: "competitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "track_configuration_updated_at",
                table: "competitions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_track_key",
                table: "teams",
                columns: new[] { "competition_id", "track_key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_teams_competition_id_track_key",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "track_key",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "track_configuration_json",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "track_configuration_revision",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "track_configuration_updated_at",
                table: "competitions");
        }
    }
}

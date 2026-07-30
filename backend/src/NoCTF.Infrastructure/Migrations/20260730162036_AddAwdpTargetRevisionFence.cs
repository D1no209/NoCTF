using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdpTargetRevisionFence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "source_challenge_definition_revision",
                table: "runtime_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source_competition_challenge_revision",
                table: "runtime_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "source_competition_configuration_revision",
                table: "runtime_instances",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source_challenge_definition_revision",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "source_competition_challenge_revision",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "source_competition_configuration_revision",
                table: "runtime_instances");
        }
    }
}

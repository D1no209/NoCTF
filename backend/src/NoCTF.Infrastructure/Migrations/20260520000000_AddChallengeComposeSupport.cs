using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChallengeComposeSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContainerMode",
                table: "Challenges",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ComposeProjectName",
                table: "Challenges",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ComposeYaml",
                table: "Challenges",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContainerMode",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "ComposeProjectName",
                table: "Challenges");

            migrationBuilder.DropColumn(
                name: "ComposeYaml",
                table: "Challenges");
        }
    }
}

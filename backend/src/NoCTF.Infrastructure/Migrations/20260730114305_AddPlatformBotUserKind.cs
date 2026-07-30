using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformBotUserKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "kind",
                table: "users",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "users");
        }
    }
}

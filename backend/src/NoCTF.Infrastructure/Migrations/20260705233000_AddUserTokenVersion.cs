using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    public partial class AddUserTokenVersion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TokenVersion",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokenVersion",
                table: "Users");
        }
    }
}

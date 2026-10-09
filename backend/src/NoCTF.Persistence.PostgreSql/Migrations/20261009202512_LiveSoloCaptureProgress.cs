using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloCaptureProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "last_fragment_imported_at",
                table: "live_solo_program_captures",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "stalled_at",
                table: "live_solo_program_captures",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "last_fragment_imported_at",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "stalled_at",
                table: "live_solo_program_captures");
        }
    }
}

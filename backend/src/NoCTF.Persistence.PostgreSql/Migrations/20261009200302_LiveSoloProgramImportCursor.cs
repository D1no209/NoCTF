using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloProgramImportCursor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "imported_through",
                table: "live_solo_program_captures",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "next_segment_sequence",
                table: "live_solo_program_captures",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "imported_through",
                table: "live_solo_program_captures");

            migrationBuilder.DropColumn(
                name: "next_segment_sequence",
                table: "live_solo_program_captures");
        }
    }
}

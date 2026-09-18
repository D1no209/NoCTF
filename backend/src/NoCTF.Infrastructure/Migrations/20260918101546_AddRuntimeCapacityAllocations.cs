using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeCapacityAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "capacity_allocations",
                table: "runtime_instances",
                type: "jsonb",
                nullable: false,
                defaultValue: "{\"schemaVersion\":1,\"items\":[]}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "capacity_allocations",
                table: "runtime_instances");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class IndexRuntimeCapacityResourceDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_runtime_capacity_allocations_resource_domain",
                table: "runtime_capacity_allocations",
                column: "resource_domain");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_capacity_allocations_resource_domain",
                table: "runtime_capacity_allocations");
        }
    }
}

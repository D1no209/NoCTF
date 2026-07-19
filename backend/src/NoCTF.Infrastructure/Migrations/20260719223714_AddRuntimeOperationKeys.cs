using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeOperationKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OperationKey",
                table: "runtime_operations",
                type: "character varying(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_runtime_operations_CompetitionId_OperationKey",
                table: "runtime_operations",
                columns: new[] { "CompetitionId", "OperationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_runtime_operations_CompetitionId_OperationKey",
                table: "runtime_operations");

            migrationBuilder.DropColumn(
                name: "OperationKey",
                table: "runtime_operations");
        }
    }
}

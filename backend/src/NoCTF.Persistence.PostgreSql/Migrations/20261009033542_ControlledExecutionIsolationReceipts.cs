using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ControlledExecutionIsolationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "execution_scope_id",
                table: "runtime_receipts",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "isolation_state",
                table: "runtime_receipts",
                type: "smallint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "execution_scope_id",
                table: "runtime_receipts");

            migrationBuilder.DropColumn(
                name: "isolation_state",
                table: "runtime_receipts");
        }
    }
}

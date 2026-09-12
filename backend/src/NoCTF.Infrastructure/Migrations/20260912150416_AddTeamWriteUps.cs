using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamWriteUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "write_up_file_id",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "write_up_submitted_at",
                table: "teams",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "write_up_submitted_by_user_id",
                table: "teams",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_write_up_file_id",
                table: "teams",
                column: "write_up_file_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_teams_write_up_metadata_complete",
                table: "teams",
                sql: "(write_up_file_id IS NULL AND write_up_submitted_by_user_id IS NULL AND write_up_submitted_at IS NULL) OR (write_up_file_id IS NOT NULL AND write_up_submitted_by_user_id IS NOT NULL AND write_up_submitted_at IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_teams_files_write_up_file_id",
                table: "teams",
                column: "write_up_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_teams_files_write_up_file_id",
                table: "teams");

            migrationBuilder.DropIndex(
                name: "ix_teams_write_up_file_id",
                table: "teams");

            migrationBuilder.DropCheckConstraint(
                name: "ck_teams_write_up_metadata_complete",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "write_up_file_id",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "write_up_submitted_at",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "write_up_submitted_by_user_id",
                table: "teams");
        }
    }
}

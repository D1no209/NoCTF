using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profile_cover_file_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_profile_cover_file_id",
                table: "users",
                column: "profile_cover_file_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_files_profile_cover_file_id",
                table: "users",
                column: "profile_cover_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_files_profile_cover_file_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_profile_cover_file_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "profile_cover_file_id",
                table: "users");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UserWallpaperPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "wallpaper_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "wallpaper_file_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_wallpaper_file_id",
                table: "users",
                column: "wallpaper_file_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_wallpaper_enabled",
                table: "users",
                sql: "NOT \"wallpaper_enabled\" OR \"wallpaper_file_id\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_users_files_wallpaper_file_id",
                table: "users",
                column: "wallpaper_file_id",
                principalTable: "files",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_files_wallpaper_file_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_wallpaper_file_id",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_wallpaper_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "wallpaper_enabled",
                table: "users");

            migrationBuilder.DropColumn(
                name: "wallpaper_file_id",
                table: "users");
        }
    }
}

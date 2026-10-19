using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ScoreboardOptimisticAccessAndAdmissionIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "scoreboard_access_revision",
                table: "competitions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_key_hash_expires_at",
                table: "request_admission_leases",
                columns: new[] { "key_hash", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_request_admission_leases_key_hash_expires_at",
                table: "request_admission_leases");

            migrationBuilder.DropColumn(
                name: "scoreboard_access_revision",
                table: "competitions");
        }
    }
}

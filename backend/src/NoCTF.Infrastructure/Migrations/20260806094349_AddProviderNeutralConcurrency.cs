using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderNeutralConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "users",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "teams",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "critical_section_version",
                table: "teams",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<Guid>(
                name: "evaluation_claim_id",
                table: "submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "active_slot",
                table: "data_exports",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "competitions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "competition_challenges",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "critical_section_version",
                table: "competition_challenges",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "concurrency_version",
                table: "challenges",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "ix_data_exports_requested_by_user_id_scope_active_slot",
                table: "data_exports",
                columns: new[] { "requested_by_user_id", "scope", "active_slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_data_exports_requested_by_user_id_scope_active_slot",
                table: "data_exports");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "users");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "critical_section_version",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "evaluation_claim_id",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "active_slot",
                table: "data_exports");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "critical_section_version",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "concurrency_version",
                table: "challenges");
        }
    }
}

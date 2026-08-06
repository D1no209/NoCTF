using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class NarrowProviderNeutralConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_published_ports_competition_id_host_port",
                table: "runtime_published_ports");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_published_ports_host_port",
                table: "runtime_published_ports");

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
                name: "critical_section_version",
                table: "competition_challenges",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_published_ports_host_port",
                table: "runtime_published_ports",
                sql: "host_port BETWEEN 1 AND 65535");

            migrationBuilder.CreateIndex(
                name: "ix_data_exports_requested_by_user_id_scope_active_slot",
                table: "data_exports",
                columns: new[] { "requested_by_user_id", "scope", "active_slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_published_ports_host_port",
                table: "runtime_published_ports");

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
                name: "critical_section_version",
                table: "competition_challenges");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_published_ports_competition_id_host_port",
                table: "runtime_published_ports",
                columns: new[] { "competition_id", "host_port" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_published_ports_host_port",
                table: "runtime_published_ports",
                sql: "host_port BETWEEN 61000 AND 64999");
        }
    }
}

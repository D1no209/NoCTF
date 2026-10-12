using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdCheckerEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name",
                table: "awd_round_store_budgets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "attempt_deadline",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "attempt_id",
                table: "awd_fact_details",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "attempt_started_at",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "manifest_complete",
                table: "awd_fact_details",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "received_at",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sample_active_ticks",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "sample_deadline_active_ticks",
                table: "awd_fact_details",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name",
                table: "awd_round_store_budgets");

            migrationBuilder.DropColumn(
                name: "attempt_deadline",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "attempt_id",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "attempt_started_at",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "manifest_complete",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "received_at",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "sample_active_ticks",
                table: "awd_fact_details");

            migrationBuilder.DropColumn(
                name: "sample_deadline_active_ticks",
                table: "awd_fact_details");
        }
    }
}

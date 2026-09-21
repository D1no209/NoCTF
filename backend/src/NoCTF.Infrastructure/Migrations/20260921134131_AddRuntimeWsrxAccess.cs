using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeWsrxAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "access_endpoints_json",
                table: "runtime_instances",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "access_mode",
                table: "runtime_instances",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "traffic_capture_enabled",
                table: "runtime_instances",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "traffic_capture_limit_bytes",
                table: "runtime_instances",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "traffic_capture_reserved_bytes",
                table: "runtime_instances",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<short>(
                name: "runtime_access_mode",
                table: "competitions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "traffic_capture_enabled",
                table: "competitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "traffic_capture_limit_bytes",
                table: "competitions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_traffic_capture",
                table: "runtime_instances",
                sql: "(traffic_capture_limit_bytes IS NULL OR traffic_capture_limit_bytes > 0) AND traffic_capture_reserved_bytes >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_competitions_traffic_capture_limit",
                table: "competitions",
                sql: "traffic_capture_limit_bytes IS NULL OR traffic_capture_limit_bytes > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_traffic_capture",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_competitions_traffic_capture_limit",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "access_endpoints_json",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "access_mode",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "traffic_capture_enabled",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "traffic_capture_limit_bytes",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "traffic_capture_reserved_bytes",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "runtime_access_mode",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "traffic_capture_enabled",
                table: "competitions");

            migrationBuilder.DropColumn(
                name: "traffic_capture_limit_bytes",
                table: "competitions");
        }
    }
}

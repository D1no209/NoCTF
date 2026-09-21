using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePublicGateway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "public_gateway_connector_id",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_direct_host_override",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_direct_origins",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_enabled",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_max_ports",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_origin",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "public_gateway_runtime_host",
                table: "platform_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "public_gateway_connector_id",
                table: "platform_settings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "public_gateway_direct_host_override",
                table: "platform_settings",
                type: "character varying(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string[]>(
                name: "public_gateway_direct_origins",
                table: "platform_settings",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.AddColumn<bool>(
                name: "public_gateway_enabled",
                table: "platform_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "public_gateway_max_ports",
                table: "platform_settings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "public_gateway_origin",
                table: "platform_settings",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "public_gateway_runtime_host",
                table: "platform_settings",
                type: "character varying(253)",
                maxLength: 253,
                nullable: false,
                defaultValue: "");

            migrationBuilder.UpdateData(
                table: "platform_settings",
                keyColumn: "id",
                keyValue: (short)1,
                columns: new[] { "public_gateway_connector_id", "public_gateway_direct_host_override", "public_gateway_direct_origins", "public_gateway_enabled", "public_gateway_max_ports", "public_gateway_origin", "public_gateway_runtime_host" },
                values: new object[] { "", null, new string[0], false, 0, "", "" });
        }
    }
}

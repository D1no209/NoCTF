using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class NamedContainerServices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_runtime_capabilities");

            migrationBuilder.DropTable(
                name: "challenge_runtime_command_items");

            migrationBuilder.DropTable(
                name: "challenge_runtime_internal_ports");

            migrationBuilder.DropTable(
                name: "challenge_runtime_key_values");

            migrationBuilder.DropTable(
                name: "challenge_runtime_port_mappings");

            migrationBuilder.DropTable(
                name: "compose_service_resources");

            migrationBuilder.DropTable(
                name: "runtime_receipt_ports");

            migrationBuilder.DropColumn(
                name: "compose_runtime_receipt_public_host",
                table: "runtime_receipts");

            migrationBuilder.DropColumn(
                name: "container_runtime_receipt_network_id",
                table: "runtime_receipts");

            migrationBuilder.DropColumn(
                name: "status",
                table: "runtime_receipts");

            migrationBuilder.DropColumn(
                name: "compose_yaml",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "flag_environment_variable_name",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "image",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "limits_nano_cpus",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "security_no_new_privileges",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "security_readonly_rootfs",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "security_run_as_non_root",
                table: "challenge_runtime_templates");

            migrationBuilder.DropColumn(
                name: "checker_allow_root",
                table: "challenge_definitions");

            migrationBuilder.RenameColumn(
                name: "compose_runtime_receipt_created_at",
                table: "runtime_receipts",
                newName: "container_runtime_receipt_created_at");

            migrationBuilder.RenameColumn(
                name: "resource_id",
                table: "runtime_receipts",
                newName: "owned_network_id");

            migrationBuilder.RenameColumn(
                name: "internal_host",
                table: "runtime_receipts",
                newName: "discovery_service_name");

            migrationBuilder.RenameColumn(
                name: "limit_nano_cpus",
                table: "runtime_capacity_allocations",
                newName: "limit_cpu_millicores");

            migrationBuilder.RenameColumn(
                name: "budget_nano_cpus",
                table: "runtime_capacity_allocations",
                newName: "budget_cpu_millicores");

            migrationBuilder.AlterColumn<long>(
                name: "limits_pids_limit",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<long>(
                name: "limits_memory_bytes",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<bool>(
                name: "has_explicit_limits",
                table: "challenge_runtime_templates",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AddColumn<long>(
                name: "limits_cpu_millicores",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "challenge_runtime_services",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    cpu_cores = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    memory_mi_b = table.Column<long>(type: "bigint", nullable: false),
                    flag_environment_variable_name = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_services", x => new { x.challenge_id, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_services_challenge_runtime_templates_chal",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    internal_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_services_runtime_receipts_runtime_instance_",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_receipts",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_commands",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    is_argument = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_commands", x => new { x.challenge_id, x.service_name, x.is_argument, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_commands_challenge_runtime_servic",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_environment",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_environment", x => new { x.challenge_id, x.service_name, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_environment_challenge_runtime_ser",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_internal_ports",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_internal_ports", x => new { x.challenge_id, x.service_name, x.port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_internal_ports_challenge_runtime_",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_service_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_service_ports", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_service_ports_runtime_receipt_services_serv",
                        column: x => x.service_id,
                        principalTable: "runtime_receipt_services",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_runtime_services_challenge_id_position",
                table: "challenge_runtime_services",
                columns: new[] { "challenge_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_service_ports_service_id_container_port",
                table: "runtime_receipt_service_ports",
                columns: new[] { "service_id", "container_port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_services_runtime_instance_id_name",
                table: "runtime_receipt_services",
                columns: new[] { "runtime_instance_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_runtime_service_commands");

            migrationBuilder.DropTable(
                name: "challenge_runtime_service_environment");

            migrationBuilder.DropTable(
                name: "challenge_runtime_service_internal_ports");

            migrationBuilder.DropTable(
                name: "runtime_receipt_service_ports");

            migrationBuilder.DropTable(
                name: "challenge_runtime_services");

            migrationBuilder.DropTable(
                name: "runtime_receipt_services");

            migrationBuilder.DropColumn(
                name: "limits_cpu_millicores",
                table: "challenge_runtime_templates");

            migrationBuilder.RenameColumn(
                name: "container_runtime_receipt_created_at",
                table: "runtime_receipts",
                newName: "compose_runtime_receipt_created_at");

            migrationBuilder.RenameColumn(
                name: "owned_network_id",
                table: "runtime_receipts",
                newName: "resource_id");

            migrationBuilder.RenameColumn(
                name: "discovery_service_name",
                table: "runtime_receipts",
                newName: "internal_host");

            migrationBuilder.RenameColumn(
                name: "limit_cpu_millicores",
                table: "runtime_capacity_allocations",
                newName: "limit_nano_cpus");

            migrationBuilder.RenameColumn(
                name: "budget_cpu_millicores",
                table: "runtime_capacity_allocations",
                newName: "budget_nano_cpus");

            migrationBuilder.AddColumn<string>(
                name: "compose_runtime_receipt_public_host",
                table: "runtime_receipts",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "container_runtime_receipt_network_id",
                table: "runtime_receipts",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "status",
                table: "runtime_receipts",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "limits_pids_limit",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "limits_memory_bytes",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "has_explicit_limits",
                table: "challenge_runtime_templates",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "compose_yaml",
                table: "challenge_runtime_templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "flag_environment_variable_name",
                table: "challenge_runtime_templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image",
                table: "challenge_runtime_templates",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "limits_nano_cpus",
                table: "challenge_runtime_templates",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "security_no_new_privileges",
                table: "challenge_runtime_templates",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "security_readonly_rootfs",
                table: "challenge_runtime_templates",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "security_run_as_non_root",
                table: "challenge_runtime_templates",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "checker_allow_root",
                table: "challenge_definitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "challenge_runtime_capabilities",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    add = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_capabilities", x => new { x.challenge_id, x.add, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_capabilities_challenge_runtime_templates_",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_command_items",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_command_items", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_command_items_challenge_runtime_templates",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_internal_ports",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_internal_ports", x => new { x.challenge_id, x.port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_internal_ports_challenge_runtime_template",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_key_values",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_key_values", x => new { x.challenge_id, x.kind, x.key });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_key_values_challenge_runtime_templates_ch",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_port_mappings",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_port_mappings", x => new { x.challenge_id, x.container_port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_port_mappings_challenge_runtime_templates",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compose_service_resources",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "text", nullable: false),
                    limits_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    limits_nano_cpus = table.Column<long>(type: "bigint", nullable: false),
                    limits_pids_limit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compose_service_resources", x => new { x.challenge_id, x.service_name });
                    table.ForeignKey(
                        name: "fk_compose_service_resources_challenge_runtime_templates_chall",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_ports", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_ports_runtime_receipts_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_receipts",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_ports_runtime_instance_id",
                table: "runtime_receipt_ports",
                column: "runtime_instance_id");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class ContainerCapabilityExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_check_requirements");

            migrationBuilder.DropColumn(
                name: "container_port",
                table: "challenge_capabilities");

            migrationBuilder.DropColumn(
                name: "guest_port",
                table: "challenge_capabilities");

            migrationBuilder.DropColumn(
                name: "protocol",
                table: "challenge_capabilities");

            migrationBuilder.DropColumn(
                name: "url_template",
                table: "challenge_capabilities");

            migrationBuilder.DropColumn(
                name: "vm_id",
                table: "challenge_capabilities");

            migrationBuilder.AddColumn<string>(
                name: "control_capability_definition_image",
                table: "challenge_capabilities",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "challenge_control_command",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_control_command", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_control_command_challenge_capabilities_challenge_",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_control_environment",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_control_environment", x => new { x.challenge_id, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_control_environment_challenge_capabilities_challe",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "control_execution_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deadline = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    observed_at = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<long>(type: "bigint", nullable: true),
                    work_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    finalized_at = table.Column<long>(type: "bigint", nullable: true),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    controller_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    failure = table.Column<short>(type: "smallint", nullable: true),
                    reported_failure = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_control_execution_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_control_execution_details_gameplay_facts_id",
                        column: x => x.id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_control_execution_details_runtime_instances_runtime_instanc",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "template_control_execution_details",
                columns: table => new
                {
                    template_capability_test_runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    deadline = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    observed_at = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<long>(type: "bigint", nullable: true),
                    work_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    finalized_at = table.Column<long>(type: "bigint", nullable: true),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    controller_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    failure = table.Column<short>(type: "smallint", nullable: true),
                    reported_failure = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_control_execution_details", x => x.template_capability_test_runtime_instance_id);
                    table.ForeignKey(
                        name: "fk_template_control_execution_details_template_capability_test",
                        column: x => x.template_capability_test_runtime_instance_id,
                        principalTable: "template_capability_tests",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_control_command_challenge_id_capability_kind",
                table: "challenge_control_command",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_control_environment_challenge_id_capability_kind",
                table: "challenge_control_environment",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_control_execution_details_execution_id",
                table: "control_execution_details",
                column: "execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_control_execution_details_runtime_instance_id",
                table: "control_execution_details",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_control_execution_details_execution_id",
                table: "template_control_execution_details",
                column: "execution_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "challenge_control_command");

            migrationBuilder.DropTable(
                name: "challenge_control_environment");

            migrationBuilder.DropTable(
                name: "control_execution_details");

            migrationBuilder.DropTable(
                name: "template_control_execution_details");

            migrationBuilder.DropColumn(
                name: "control_capability_definition_image",
                table: "challenge_capabilities");

            migrationBuilder.AddColumn<int>(
                name: "container_port",
                table: "challenge_capabilities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "guest_port",
                table: "challenge_capabilities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "protocol",
                table: "challenge_capabilities",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "url_template",
                table: "challenge_capabilities",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "vm_id",
                table: "challenge_capabilities",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "challenge_check_requirements",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_check_requirements", x => new { x.challenge_id, x.kind });
                    table.ForeignKey(
                        name: "fk_challenge_check_requirements_challenge_capabilities_challen",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_check_requirements_challenge_id_capability_kind",
                table: "challenge_check_requirements",
                columns: new[] { "challenge_id", "capability_kind" });
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimePublishedPortHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "ak_runtime_instances_id_competition_id",
                table: "runtime_instances",
                columns: new[] { "id", "competition_id" });

            migrationBuilder.CreateTable(
                name: "runtime_published_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    allocated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_published_ports", x => x.id);
                    table.CheckConstraint("ck_runtime_published_ports_container_port", "container_port BETWEEN 1 AND 65535");
                    table.CheckConstraint("ck_runtime_published_ports_host_port", "host_port BETWEEN 61000 AND 64999");
                    table.ForeignKey(
                        name: "fk_runtime_published_ports_runtime_instances_runtime_instance_",
                        columns: x => new { x.runtime_instance_id, x.competition_id },
                        principalTable: "runtime_instances",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_published_ports_competition_id_host_port",
                table: "runtime_published_ports",
                columns: new[] { "competition_id", "host_port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_published_ports_runtime_instance_id_competition_id",
                table: "runtime_published_ports",
                columns: new[] { "runtime_instance_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_published_ports_runtime_instance_id_service_name_co",
                table: "runtime_published_ports",
                columns: new[] { "runtime_instance_id", "service_name", "container_port" },
                unique: true)
                .Annotation("Npgsql:NullsDistinct", false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "runtime_published_ports");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_runtime_instances_id_competition_id",
                table: "runtime_instances");
        }
    }
}

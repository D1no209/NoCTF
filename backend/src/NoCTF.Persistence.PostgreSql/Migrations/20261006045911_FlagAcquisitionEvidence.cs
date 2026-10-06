using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class FlagAcquisitionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flag_acquisition_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    required = table.Column<short>(type: "smallint", nullable: false),
                    acquired = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    captured_at = table.Column<long>(type: "bigint", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_started_at = table.Column<long>(type: "bigint", nullable: true),
                    attachment_download_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_downloaded_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flag_acquisition_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_flag_acquisition_evidence_gameplay_facts_id",
                        column: x => x.id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flag_acquisition_evidence");
        }
    }
}

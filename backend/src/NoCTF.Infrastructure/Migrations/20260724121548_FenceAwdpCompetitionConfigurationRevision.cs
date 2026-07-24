using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FenceAwdpCompetitionConfigurationRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "competition_configuration_revision",
                table: "runtime_instances",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE runtime_instances AS runtime
                SET competition_configuration_revision = competition.configuration_revision
                FROM competitions AS competition
                WHERE runtime.purpose = 1
                  AND runtime.competition_id = competition.id;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_competition_revision",
                table: "runtime_instances",
                sql: "(purpose = 1) = (competition_configuration_revision IS NOT NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_competition_revision",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "competition_configuration_revision",
                table: "runtime_instances");
        }
    }
}

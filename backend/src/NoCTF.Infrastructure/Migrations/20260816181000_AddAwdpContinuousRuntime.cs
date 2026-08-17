using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAwdpContinuousRuntime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gameplay_facts_victim",
                table: "gameplay_facts");

            migrationBuilder.AddColumn<short>(
                name: "awdp_fix_stage",
                table: "runtime_instances",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "purpose IN (0, 2, 3) AND state IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances",
                sql: "awdp_fix_stage IS NULL OR purpose = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gameplay_facts_victim",
                table: "gameplay_facts",
                sql: "victim_team_id IS NULL OR kind IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_specification_kind_specification_id",
                table: "challenge_flags",
                columns: new[] { "specification_kind", "specification_id" },
                unique: true,
                filter: "specification_kind = 4 AND deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_gameplay_facts_victim",
                table: "gameplay_facts");

            migrationBuilder.DropIndex(
                name: "ix_challenge_flags_specification_kind_specification_id",
                table: "challenge_flags");

            migrationBuilder.DropColumn(
                name: "awdp_fix_stage",
                table: "runtime_instances");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "purpose IN (0, 2) AND state IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_gameplay_facts_victim",
                table: "gameplay_facts",
                sql: "victim_team_id IS NULL OR kind = 0");
        }
    }
}

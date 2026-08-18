using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BindAwdpFixTarget : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_generati",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_gameplay_fact",
                table: "runtime_instances");

            migrationBuilder.AddColumn<Guid>(
                name: "runtime_instance_id",
                table: "patch_uploads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_purpose_",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id", "purpose", "generation" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_bound_attempt",
                table: "runtime_instances",
                sql: "purpose <> 1 OR awdp_fix_stage IN (0, 1) OR gameplay_fact_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances",
                sql: "(awdp_fix_stage IS NOT NULL) = (purpose = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_gameplay_fact",
                table: "runtime_instances",
                sql: "gameplay_fact_id IS NULL OR purpose = 1");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_runtime_instance_id",
                table: "patch_uploads",
                column: "runtime_instance_id",
                unique: true,
                filter: "runtime_instance_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_patch_uploads_runtime_instances_runtime_instance_id",
                table: "patch_uploads",
                column: "runtime_instance_id",
                principalTable: "runtime_instances",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_patch_uploads_runtime_instances_runtime_instance_id",
                table: "patch_uploads");

            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_purpose_",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_bound_attempt",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_awdp_gameplay_fact",
                table: "runtime_instances");

            migrationBuilder.DropIndex(
                name: "ix_patch_uploads_runtime_instance_id",
                table: "patch_uploads");

            migrationBuilder.DropColumn(
                name: "runtime_instance_id",
                table: "patch_uploads");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_generati",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id", "generation" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_fix_stage",
                table: "runtime_instances",
                sql: "awdp_fix_stage IS NULL OR purpose = 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_awdp_gameplay_fact",
                table: "runtime_instances",
                sql: "(purpose = 1) = (gameplay_fact_id IS NOT NULL)");
        }
    }
}

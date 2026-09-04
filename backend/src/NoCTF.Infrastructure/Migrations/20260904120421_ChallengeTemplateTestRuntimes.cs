using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ChallengeTemplateTestRuntimes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropUniqueConstraint(
                name: "ak_runtime_instances_id_competition_id",
                table: "runtime_instances");

            migrationBuilder.AlterColumn<Guid>(
                name: "competition_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "competition_challenge_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "challenge_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "test_flag_delivery",
                table: "runtime_instances",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "test_flag_state",
                table: "runtime_instances",
                type: "smallint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_challenge_id",
                table: "runtime_instances",
                column: "challenge_id",
                unique: true,
                filter: "purpose = 4 AND state IN (0, 1, 2)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_scope",
                table: "runtime_instances",
                sql: "(purpose = 4 AND challenge_id IS NOT NULL AND competition_id IS NULL AND competition_challenge_id IS NULL AND team_id IS NULL AND gameplay_fact_id IS NULL) OR (purpose <> 4 AND challenge_id IS NULL AND competition_id IS NOT NULL AND competition_challenge_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_runtime_instances_test_flag",
                table: "runtime_instances",
                sql: "(purpose = 4) = (test_flag_delivery IS NOT NULL AND test_flag_state IS NOT NULL)");

            migrationBuilder.AddForeignKey(
                name: "fk_runtime_instances_challenges_challenge_id",
                table: "runtime_instances",
                column: "challenge_id",
                principalTable: "challenges",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_runtime_instances_challenges_challenge_id",
                table: "runtime_instances");

            migrationBuilder.DropIndex(
                name: "ix_runtime_instances_challenge_id",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_scope",
                table: "runtime_instances");

            migrationBuilder.DropCheckConstraint(
                name: "ck_runtime_instances_test_flag",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "challenge_id",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "test_flag_delivery",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "test_flag_state",
                table: "runtime_instances");

            migrationBuilder.AlterColumn<Guid>(
                name: "competition_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "competition_challenge_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_runtime_instances_id_competition_id",
                table: "runtime_instances",
                columns: new[] { "id", "competition_id" });
        }
    }
}

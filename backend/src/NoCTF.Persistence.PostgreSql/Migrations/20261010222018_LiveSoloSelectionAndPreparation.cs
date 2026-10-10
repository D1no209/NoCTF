using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloSelectionAndPreparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "question_group_stamp",
                table: "live_solo_rounds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_by_user_id",
                table: "live_solo_rounds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "selection_kind",
                table: "live_solo_rounds",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selection_revision",
                table: "live_solo_rounds",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "static_flag_evidence_required",
                table: "live_solo_rounds",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "environment_required",
                table: "live_solo_round_questions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "preparation_failure",
                table: "live_solo_round_questions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "static_flag_evidence_required",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "live_solo_round_selection_audits",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_group_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    selection_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_round_selection_audits", x => new { x.round_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_round_selection_audits_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_round_selection_audits_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_round_selection_risks",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    selection_position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canonical_challenge_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_round_selection_risks", x => new { x.round_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_round_selection_risks_live_solo_round_selection_a",
                        columns: x => new { x.round_id, x.selection_position },
                        principalTable: "live_solo_round_selection_audits",
                        principalColumns: new[] { "round_id", "position" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_round_selection_risks_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_selected_by_user_id",
                table: "live_solo_rounds",
                column: "selected_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_selection_audits_actor_user_id",
                table: "live_solo_round_selection_audits",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_selection_risks_round_id_selection_position",
                table: "live_solo_round_selection_risks",
                columns: new[] { "round_id", "selection_position" });

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_rounds_users_selected_by_user_id",
                table: "live_solo_rounds",
                column: "selected_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_users_selected_by_user_id",
                table: "live_solo_rounds");

            migrationBuilder.DropTable(
                name: "live_solo_round_selection_risks");

            migrationBuilder.DropTable(
                name: "live_solo_round_selection_audits");

            migrationBuilder.DropIndex(
                name: "ix_live_solo_rounds_selected_by_user_id",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "question_group_stamp",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "selected_by_user_id",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "selection_kind",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "selection_revision",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "static_flag_evidence_required",
                table: "live_solo_rounds");

            migrationBuilder.DropColumn(
                name: "environment_required",
                table: "live_solo_round_questions");

            migrationBuilder.DropColumn(
                name: "preparation_failure",
                table: "live_solo_round_questions");

            migrationBuilder.DropColumn(
                name: "static_flag_evidence_required",
                table: "competition_mode_configurations");
        }
    }
}

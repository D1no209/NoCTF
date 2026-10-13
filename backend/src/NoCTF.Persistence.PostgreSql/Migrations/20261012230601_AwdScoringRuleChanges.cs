using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdScoringRuleChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "awd_scoring_rule_changes",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    first_round = table.Column<int>(type: "integer", nullable: false),
                    last_round = table.Column<int>(type: "integer", nullable: false),
                    timing = table.Column<short>(type: "smallint", nullable: false),
                    applied_at = table.Column<long>(type: "bigint", nullable: false),
                    policy_attack_points = table.Column<long>(type: "bigint", nullable: false),
                    policy_attack_reward_mode = table.Column<short>(type: "smallint", nullable: false),
                    policy_attack_settlement = table.Column<short>(type: "smallint", nullable: false),
                    policy_attack_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_composition = table.Column<short>(type: "smallint", nullable: false),
                    policy_confidentiality_points = table.Column<long>(type: "bigint", nullable: false),
                    policy_consecutive_failure_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_defense_mode = table.Column<short>(type: "smallint", nullable: false),
                    policy_defense_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_maximum_rank_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_maximum_round_penalty = table.Column<long>(type: "bigint", nullable: true),
                    policy_minimum_attack_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_mumble_penalty = table.Column<long>(type: "bigint", nullable: false),
                    policy_mumble_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_mumble_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_offline_penalty = table.Column<long>(type: "bigint", nullable: false),
                    policy_offline_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_offline_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_ok_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_penalty_mode = table.Column<short>(type: "smallint", nullable: false),
                    policy_recovering_penalty = table.Column<long>(type: "bigint", nullable: false),
                    policy_recovering_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_recovering_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_scarcity_alpha = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_service_healthy_points = table.Column<long>(type: "bigint", nullable: false),
                    policy_sla_basis = table.Column<short>(type: "smallint", nullable: false),
                    policy_sla_settlement = table.Column<short>(type: "smallint", nullable: false),
                    policy_sla_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    policy_victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_scoring_rule_changes", x => x.event_id);
                    table.ForeignKey(
                        name: "fk_awd_scoring_rule_changes_competition_challenges_competition",
                        columns: x => new { x.competition_challenge_id, x.competition_id },
                        principalTable: "competition_challenges",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_scoring_rule_changes_competition_events_event_id",
                        column: x => x.event_id,
                        principalTable: "competition_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awd_scoring_rule_changes_competition_challenge_id_competiti",
                table: "awd_scoring_rule_changes",
                columns: new[] { "competition_challenge_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_scoring_rule_changes_competition_challenge_id_sequence",
                table: "awd_scoring_rule_changes",
                columns: new[] { "competition_challenge_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_scoring_rule_changes_competition_id_applied_at",
                table: "awd_scoring_rule_changes",
                columns: new[] { "competition_id", "applied_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "awd_scoring_rule_changes");
        }
    }
}

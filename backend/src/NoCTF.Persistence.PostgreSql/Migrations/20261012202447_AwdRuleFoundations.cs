using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class AwdRuleFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<short>(
                name: "awd_options_attack_settlement",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_attack_weight",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_callback_grace_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_composition",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_confidentiality_points",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_consecutive_failure_multiplier",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_defense_mode",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_defense_weight",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_final_check_budget_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_flag_lifetime",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_flag_lifetime_unit",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_flags_per_store",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_generation_interval",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_generation_policy",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_historical_lifetime",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_historical_lifetime_unit",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "awd_options_link_historical_window",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_maximum_rank_multiplier",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_maximum_round_penalty",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_minimum_attack_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_minimum_total_score",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_mumble_penalty",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_mumble_penalty_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_mumble_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_offline_penalty",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_offline_penalty_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_offline_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_ok_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_penalty_mode",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_preset",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_options_recovering_penalty",
                table: "competition_mode_configurations",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_recovering_penalty_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_recovering_ratio",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_retry_count",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_retry_interval_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_round_gap_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_scarcity_alpha",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_sla_basis",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_options_sla_settlement",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_options_sla_weight",
                table: "competition_mode_configurations",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_submission_tail_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_options_total_rounds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_attack_settlement",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_callback_grace_seconds",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_overrides_confidentiality_points",
                table: "competition_challenge_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_consecutive_failure_multiplier",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_defense_mode",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_final_check_budget_seconds",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_flag_lifetime",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_flag_lifetime_unit",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_flags_per_store",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_generation_interval",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_generation_policy",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_historical_lifetime",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_historical_lifetime_unit",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "awd_overrides_link_historical_window",
                table: "competition_challenge_rules",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_maximum_rank_multiplier",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_overrides_maximum_round_penalty",
                table: "competition_challenge_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_minimum_attack_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_overrides_mumble_penalty",
                table: "competition_challenge_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_mumble_penalty_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_mumble_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_overrides_offline_penalty",
                table: "competition_challenge_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_offline_penalty_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_offline_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_ok_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_penalty_mode",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_preset",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "awd_overrides_recovering_penalty",
                table: "competition_challenge_rules",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_recovering_penalty_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_recovering_ratio",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_retry_count",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "awd_overrides_retry_interval_seconds",
                table: "competition_challenge_rules",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "awd_overrides_scarcity_alpha",
                table: "competition_challenge_rules",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_sla_basis",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "awd_overrides_sla_settlement",
                table: "competition_challenge_rules",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "ak_teams_id_competition_id",
                table: "teams",
                columns: new[] { "id", "competition_id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_competition_challenges_id_competition_id",
                table: "competition_challenges",
                columns: new[] { "id", "competition_id" });

            migrationBuilder.CreateTable(
                name: "awd_flag_stores",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_service_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    delivery_mode = table.Column<short>(type: "smallint", nullable: false),
                    injection_command = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    injection_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    preserves_historical_flags = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_flag_stores", x => new { x.challenge_id, x.id });
                    table.ForeignKey(
                        name: "fk_awd_flag_stores_challenge_definition_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenge_definitions",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<long>(type: "bigint", nullable: false),
                    ends_at = table.Column<long>(type: "bigint", nullable: false),
                    active_start_ticks = table.Column<long>(type: "bigint", nullable: false),
                    active_end_ticks = table.Column<long>(type: "bigint", nullable: false),
                    gap_seconds = table.Column<int>(type: "integer", nullable: false),
                    missed = table.Column<bool>(type: "boolean", nullable: false),
                    partial = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_rounds", x => x.id);
                    table.UniqueConstraint("ak_awd_rounds_id_competition_id", x => new { x.id, x.competition_id });
                    table.ForeignKey(
                        name: "fk_awd_rounds_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_store_rules",
                columns: table => new
                {
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    flags_per_issue = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_store_rules", x => new { x.competition_challenge_id, x.store_id });
                    table.ForeignKey(
                        name: "fk_awd_store_rules_competition_challenge_rules_competition_cha",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenge_rules",
                        principalColumn: "competition_challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    effective_at = table.Column<long>(type: "bigint", nullable: false),
                    attack_reward_mode = table.Column<short>(type: "smallint", nullable: false),
                    attack_points = table.Column<long>(type: "bigint", nullable: false),
                    victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: false),
                    service_healthy_points = table.Column<long>(type: "bigint", nullable: false),
                    checker_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    eligible_team_count = table.Column<int>(type: "integer", nullable: false),
                    options_attack_settlement = table.Column<short>(type: "smallint", nullable: false),
                    options_attack_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_callback_grace_seconds = table.Column<int>(type: "integer", nullable: false),
                    options_composition = table.Column<short>(type: "smallint", nullable: false),
                    options_confidentiality_points = table.Column<long>(type: "bigint", nullable: false),
                    options_consecutive_failure_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_defense_mode = table.Column<short>(type: "smallint", nullable: false),
                    options_defense_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_final_check_budget_seconds = table.Column<int>(type: "integer", nullable: false),
                    options_flag_lifetime = table.Column<int>(type: "integer", nullable: false),
                    options_flag_lifetime_unit = table.Column<short>(type: "smallint", nullable: false),
                    options_flags_per_store = table.Column<int>(type: "integer", nullable: false),
                    options_generation_interval = table.Column<int>(type: "integer", nullable: false),
                    options_generation_policy = table.Column<short>(type: "smallint", nullable: false),
                    options_historical_lifetime = table.Column<int>(type: "integer", nullable: false),
                    options_historical_lifetime_unit = table.Column<short>(type: "smallint", nullable: false),
                    options_link_historical_window = table.Column<bool>(type: "boolean", nullable: false),
                    options_maximum_rank_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_maximum_round_penalty = table.Column<long>(type: "bigint", nullable: true),
                    options_minimum_attack_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_minimum_total_score = table.Column<long>(type: "bigint", nullable: true),
                    options_mumble_penalty = table.Column<long>(type: "bigint", nullable: false),
                    options_mumble_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_mumble_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_offline_penalty = table.Column<long>(type: "bigint", nullable: false),
                    options_offline_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_offline_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_ok_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_penalty_mode = table.Column<short>(type: "smallint", nullable: false),
                    options_preset = table.Column<short>(type: "smallint", nullable: false),
                    options_recovering_penalty = table.Column<long>(type: "bigint", nullable: false),
                    options_recovering_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_recovering_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_retry_count = table.Column<int>(type: "integer", nullable: false),
                    options_retry_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    options_round_gap_seconds = table.Column<int>(type: "integer", nullable: false),
                    options_scarcity_alpha = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_sla_basis = table.Column<short>(type: "smallint", nullable: false),
                    options_sla_settlement = table.Column<short>(type: "smallint", nullable: false),
                    options_sla_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    options_submission_tail_seconds = table.Column<int>(type: "integer", nullable: false),
                    options_total_rounds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_round_rules", x => x.id);
                    table.UniqueConstraint("ak_awd_round_rules_id_round_id_competition_challenge_id_compet", x => new { x.id, x.round_id, x.competition_challenge_id, x.competition_id });
                    table.ForeignKey(
                        name: "fk_awd_round_rules_awd_rounds_round_id_competition_id",
                        columns: x => new { x.round_id, x.competition_id },
                        principalTable: "awd_rounds",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_round_rules_competition_challenges_competition_challeng",
                        columns: x => new { x.competition_challenge_id, x.competition_id },
                        principalTable: "competition_challenges",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_fact_details",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generation_round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rules_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_id = table.Column<Guid>(type: "uuid", nullable: true),
                    execution = table.Column<short>(type: "smallint", nullable: false),
                    settlement = table.Column<short>(type: "smallint", nullable: false),
                    health = table.Column<short>(type: "smallint", nullable: true),
                    known_target_failure = table.Column<short>(type: "smallint", nullable: true),
                    reason = table.Column<short>(type: "smallint", nullable: false),
                    connection = table.Column<short>(type: "smallint", nullable: false),
                    connection_reason = table.Column<short>(type: "smallint", nullable: false),
                    functionality = table.Column<short>(type: "smallint", nullable: false),
                    functionality_reason = table.Column<short>(type: "smallint", nullable: false),
                    scheduled_at = table.Column<long>(type: "bigint", nullable: true),
                    deadline = table.Column<long>(type: "bigint", nullable: true),
                    receipt_deadline = table.Column<long>(type: "bigint", nullable: true),
                    observed_at = table.Column<long>(type: "bigint", nullable: true),
                    final_scheduled_check = table.Column<bool>(type: "boolean", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    required_flags = table.Column<int>(type: "integer", nullable: false),
                    readable_flags = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_fact_details", x => x.gameplay_fact_id);
                    table.ForeignKey(
                        name: "fk_awd_fact_details_awd_round_rules_rules_id",
                        column: x => x.rules_id,
                        principalTable: "awd_round_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_fact_details_awd_rounds_generation_round_id",
                        column: x => x.generation_round_id,
                        principalTable: "awd_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_fact_details_awd_rounds_received_round_id",
                        column: x => x.received_round_id,
                        principalTable: "awd_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_fact_details_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_fact_details_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_store_budgets",
                columns: table => new
                {
                    rules_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    flags_per_issue = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_awd_round_store_budget_rules_id_store_id", x => new { x.rules_id, x.store_id });
                    table.ForeignKey(
                        name: "fk_awd_round_store_budgets_awd_round_rules_rules_id",
                        column: x => x.rules_id,
                        principalTable: "awd_round_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_team_eligibility",
                columns: table => new
                {
                    rules_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settled_rank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_round_team_eligibility", x => new { x.rules_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_awd_round_team_eligibility_awd_round_rules_rules_id",
                        column: x => x.rules_id,
                        principalTable: "awd_round_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_check_flag_evidence",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    historical = table.Column<bool>(type: "boolean", nullable: false),
                    requires_write = table.Column<bool>(type: "boolean", nullable: false),
                    obligation = table.Column<short>(type: "smallint", nullable: false),
                    write = table.Column<short>(type: "smallint", nullable: false),
                    write_reason = table.Column<short>(type: "smallint", nullable: false),
                    read = table.Column<short>(type: "smallint", nullable: false),
                    read_reason = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_check_flag_evidence", x => new { x.gameplay_fact_id, x.flag_id });
                    table.ForeignKey(
                        name: "fk_awd_check_flag_evidence_awd_fact_details_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "awd_fact_details",
                        principalColumn: "gameplay_fact_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_awd_check_flag_evidence_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_flag_issues",
                columns: table => new
                {
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    rules_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_slot = table.Column<long>(type: "bigint", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    key_version = table.Column<int>(type: "integer", nullable: false),
                    protocol_version = table.Column<int>(type: "integer", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    delivery_state = table.Column<short>(type: "smallint", nullable: false),
                    obligation = table.Column<short>(type: "smallint", nullable: false),
                    delivery_reason = table.Column<short>(type: "smallint", nullable: false),
                    delivery_attempts = table.Column<int>(type: "integer", nullable: false),
                    issued_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    active_start_ticks = table.Column<long>(type: "bigint", nullable: false),
                    active_expiry_ticks = table.Column<long>(type: "bigint", nullable: true),
                    expiry_round = table.Column<int>(type: "integer", nullable: true),
                    historical_active_expiry_ticks = table.Column<long>(type: "bigint", nullable: true),
                    historical_expiry_round = table.Column<int>(type: "integer", nullable: true),
                    delivery_runtime_id = table.Column<Guid>(type: "uuid", nullable: true),
                    public_locator = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    protected_read_context = table.Column<string>(type: "character varying(32768)", maxLength: 32768, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_flag_issues", x => x.flag_id);
                    table.UniqueConstraint("ak_awd_flag_issues_flag_id_competition_id", x => new { x.flag_id, x.competition_id });
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_awd_round_rules_rules_id_round_id_competiti",
                        columns: x => new { x.rules_id, x.round_id, x.competition_challenge_id, x.competition_id },
                        principalTable: "awd_round_rules",
                        principalColumns: new[] { "id", "round_id", "competition_challenge_id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_awd_round_store_budget_rules_id_store_id",
                        columns: x => new { x.rules_id, x.store_id },
                        principalTable: "awd_round_store_budgets",
                        principalColumns: new[] { "rules_id", "store_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_teams_team_id_competition_id",
                        columns: x => new { x.team_id, x.competition_id },
                        principalTable: "teams",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_capture_identities",
                columns: table => new
                {
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attacking_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_capture_identities", x => new { x.flag_id, x.attacking_team_id });
                    table.ForeignKey(
                        name: "fk_awd_capture_identities_awd_flag_issues_flag_id_competition_",
                        columns: x => new { x.flag_id, x.competition_id },
                        principalTable: "awd_flag_issues",
                        principalColumns: new[] { "flag_id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_capture_identities_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_awd_capture_identities_teams_attacking_team_id_competition_",
                        columns: x => new { x.attacking_team_id, x.competition_id },
                        principalTable: "teams",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_awd_capture_identities_attacking_team_id_competition_id",
                table: "awd_capture_identities",
                columns: new[] { "attacking_team_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_capture_identities_flag_id_competition_id",
                table: "awd_capture_identities",
                columns: new[] { "flag_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_capture_identities_gameplay_fact_id",
                table: "awd_capture_identities",
                column: "gameplay_fact_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_check_flag_evidence_flag_id",
                table: "awd_check_flag_evidence",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_awd_fact_details_flag_id",
                table: "awd_fact_details",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_awd_fact_details_generation_round_id",
                table: "awd_fact_details",
                column: "generation_round_id");

            migrationBuilder.CreateIndex(
                name: "ix_awd_fact_details_received_round_id_final_scheduled_check_ex",
                table: "awd_fact_details",
                columns: new[] { "received_round_id", "final_scheduled_check", "execution" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_fact_details_rules_id",
                table: "awd_fact_details",
                column: "rules_id");

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_competition_id_competition_challenge_id_tea",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "competition_challenge_id", "team_id", "round_id", "store_id", "generation_slot", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_competition_id_state_delivery_state",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "state", "delivery_state" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_digest",
                table: "awd_flag_issues",
                column: "digest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_rules_id_round_id_competition_challenge_id_",
                table: "awd_flag_issues",
                columns: new[] { "rules_id", "round_id", "competition_challenge_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_rules_id_store_id",
                table: "awd_flag_issues",
                columns: new[] { "rules_id", "store_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_team_id_competition_id",
                table: "awd_flag_issues",
                columns: new[] { "team_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_stores_challenge_id_name",
                table: "awd_flag_stores",
                columns: new[] { "challenge_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_stores_challenge_id_position",
                table: "awd_flag_stores",
                columns: new[] { "challenge_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_round_rules_competition_challenge_id_competition_id",
                table: "awd_round_rules",
                columns: new[] { "competition_challenge_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_round_rules_round_id_competition_challenge_id_revision",
                table: "awd_round_rules",
                columns: new[] { "round_id", "competition_challenge_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_round_rules_round_id_competition_id",
                table: "awd_round_rules",
                columns: new[] { "round_id", "competition_id" });

            migrationBuilder.CreateIndex(
                name: "ix_awd_rounds_competition_id_number",
                table: "awd_rounds",
                columns: new[] { "competition_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_rounds_competition_id_starts_at_ends_at",
                table: "awd_rounds",
                columns: new[] { "competition_id", "starts_at", "ends_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "awd_capture_identities");

            migrationBuilder.DropTable(
                name: "awd_check_flag_evidence");

            migrationBuilder.DropTable(
                name: "awd_flag_stores");

            migrationBuilder.DropTable(
                name: "awd_round_team_eligibility");

            migrationBuilder.DropTable(
                name: "awd_store_rules");

            migrationBuilder.DropTable(
                name: "awd_flag_issues");

            migrationBuilder.DropTable(
                name: "awd_fact_details");

            migrationBuilder.DropTable(
                name: "awd_round_store_budgets");

            migrationBuilder.DropTable(
                name: "awd_round_rules");

            migrationBuilder.DropTable(
                name: "awd_rounds");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_teams_id_competition_id",
                table: "teams");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_competition_challenges_id_competition_id",
                table: "competition_challenges");

            migrationBuilder.DropColumn(
                name: "awd_options_attack_settlement",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_attack_weight",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_callback_grace_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_composition",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_confidentiality_points",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_consecutive_failure_multiplier",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_defense_mode",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_defense_weight",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_final_check_budget_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_flag_lifetime",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_flag_lifetime_unit",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_flags_per_store",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_generation_interval",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_generation_policy",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_historical_lifetime",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_historical_lifetime_unit",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_link_historical_window",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_maximum_rank_multiplier",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_maximum_round_penalty",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_minimum_attack_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_minimum_total_score",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_mumble_penalty",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_mumble_penalty_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_mumble_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_offline_penalty",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_offline_penalty_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_offline_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_ok_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_penalty_mode",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_preset",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_recovering_penalty",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_recovering_penalty_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_recovering_ratio",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_retry_count",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_retry_interval_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_round_gap_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_scarcity_alpha",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_sla_basis",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_sla_settlement",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_sla_weight",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_submission_tail_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_options_total_rounds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "awd_overrides_attack_settlement",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_callback_grace_seconds",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_confidentiality_points",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_consecutive_failure_multiplier",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_defense_mode",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_final_check_budget_seconds",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_flag_lifetime",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_flag_lifetime_unit",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_flags_per_store",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_generation_interval",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_generation_policy",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_historical_lifetime",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_historical_lifetime_unit",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_link_historical_window",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_maximum_rank_multiplier",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_maximum_round_penalty",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_minimum_attack_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_mumble_penalty",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_mumble_penalty_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_mumble_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_offline_penalty",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_offline_penalty_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_offline_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_ok_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_penalty_mode",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_preset",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_recovering_penalty",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_recovering_penalty_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_recovering_ratio",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_retry_count",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_retry_interval_seconds",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_scarcity_alpha",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_sla_basis",
                table: "competition_challenge_rules");

            migrationBuilder.DropColumn(
                name: "awd_overrides_sla_settlement",
                table: "competition_challenge_rules");
        }
    }
}

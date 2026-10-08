using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class LiveSoloFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "execution_scope_id",
                table: "runtime_instances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competitions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_mode_configurations",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AddColumn<short>(
                name: "bracket_format",
                table: "competition_mode_configurations",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "countdown_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "enabled",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_concurrent_matches",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_roster_members",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "maximum_viewers",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "participants_may_view_opponents",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "public_delay_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "question_interval_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "recording_enabled",
                table: "competition_mode_configurations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "recording_retention_days",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "required_wins",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "round_limit_seconds",
                table: "competition_mode_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_challenges",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_challenge_rules",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "challenges",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "challenge_definitions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(4)",
                oldMaxLength: 4);

            migrationBuilder.CreateTable(
                name: "live_solo_challenge_sources",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    copied_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_challenge_sources", x => x.challenge_id);
                    table.ForeignKey(
                        name: "fk_live_solo_challenge_sources_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_question_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    reserve = table.Column<bool>(type: "boolean", nullable: false),
                    round_limit_seconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_question_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_question_groups_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_stage_rules",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lane = table.Column<short>(type: "smallint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_stage_rules", x => new { x.competition_id, x.lane, x.stage });
                    table.ForeignKey(
                        name: "fk_live_solo_stage_rules_competition_mode_configuration_compet",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_question_group_items",
                columns: table => new
                {
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    open_offset_seconds = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_question_group_items", x => new { x.question_group_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_question_group_items_competition_challenges_compe",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_group_items_live_solo_question_groups_qu",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_active_team_slots",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_active_team_slots", x => new { x.competition_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_live_solo_active_team_slots_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_active_team_slots_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_download_evidence",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_download_evidence", x => x.gameplay_fact_id);
                    table.ForeignKey(
                        name: "fk_live_solo_download_evidence_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_match_slots",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    seed = table.Column<int>(type: "integer", nullable: true),
                    source_match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    roster_locked_at = table.Column<long>(type: "bigint", nullable: true),
                    ready_confirmed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_match_slots", x => new { x.match_id, x.side });
                    table.ForeignKey(
                        name: "fk_live_solo_match_slots_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    lane = table.Column<short>(type: "smallint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    conditional = table.Column<bool>(type: "boolean", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    adjudication_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_matches", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_matches_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_matches_teams_winner_team_id",
                        column: x => x.winner_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_media_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    room_identity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    participants_may_view_opponents = table.Column<bool>(type: "boolean", nullable: false),
                    recording_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    public_delay_seconds = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    stopped_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_media_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_media_sessions_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_question_exposures",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    public_at = table.Column<long>(type: "bigint", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_question_exposures", x => new { x.competition_id, x.canonical_challenge_id });
                    table.ForeignKey(
                        name: "fk_live_solo_question_exposures_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_exposures_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_roster_members",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    confirmed_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_roster_members", x => new { x.match_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_live_solo_roster_members_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_roster_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_roster_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    replay = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    limit_seconds = table.Column<int>(type: "integer", nullable: false),
                    countdown_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: false),
                    last_admission_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_resolved_sequence = table.Column<long>(type: "bigint", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    winning_gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    countdown_at = table.Column<long>(type: "bigint", nullable: true),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<long>(type: "bigint", nullable: true),
                    adjudication_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_rounds", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_rounds_gameplay_facts_winning_gameplay_fact_id",
                        column: x => x.winning_gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rounds_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_rounds_live_solo_question_groups_question_group_id",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rounds_teams_winner_team_id",
                        column: x => x.winner_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_media_participants",
                columns: table => new
                {
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<short>(type: "smallint", nullable: false),
                    identity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    screen_track_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    screen_state = table.Column<short>(type: "smallint", nullable: false),
                    observed_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_media_participants", x => new { x.media_session_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_live_solo_media_participants_live_solo_media_sessions_media",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_media_participants_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_media_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_program_segments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<long>(type: "bigint", nullable: false),
                    public_at = table.Column<long>(type: "bigint", nullable: false),
                    remove_after = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_segments", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_program_segments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_segments_live_solo_media_sessions_media_s",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_pause_intervals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_pause_intervals", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_pause_intervals_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_recordings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    egress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<long>(type: "bigint", nullable: true),
                    keep_until = table.Column<long>(type: "bigint", nullable: false),
                    dispute_hold = table.Column<bool>(type: "boolean", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_recordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_recordings_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_recordings_live_solo_media_sessions_media_session",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_recordings_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_recordings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_round_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    open_offset_seconds = table.Column<int>(type: "integer", nullable: false),
                    readiness = table.Column<short>(type: "smallint", nullable: false),
                    opened_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_round_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_round_questions_competition_challenges_competitio",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_round_questions_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_runtime_bindings",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<short>(type: "smallint", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_runtime_bindings", x => new { x.round_question_id, x.side });
                    table.ForeignKey(
                        name: "fk_live_solo_runtime_bindings_live_solo_round_questions_round_",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_runtime_bindings_runtime_instances_runtime_instan",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_submissions",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    admission_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_submissions", x => x.gameplay_fact_id);
                    table.ForeignKey(
                        name: "fk_live_solo_submissions_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_submissions_live_solo_round_questions_round_quest",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_submissions_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_active_team_slots_match_id",
                table: "live_solo_active_team_slots",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_active_team_slots_team_id",
                table: "live_solo_active_team_slots",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_challenge_sources_canonical_challenge_id",
                table: "live_solo_challenge_sources",
                column: "canonical_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_download_evidence_round_question_id",
                table: "live_solo_download_evidence",
                column: "round_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_slots_source_match_id",
                table: "live_solo_match_slots",
                column: "source_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_slots_team_id",
                table: "live_solo_match_slots",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_lane_stage_position",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "lane", "stage", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_current_round_id",
                table: "live_solo_matches",
                column: "current_round_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_winner_team_id",
                table: "live_solo_matches",
                column: "winner_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_participants_media_session_id_identity",
                table: "live_solo_media_participants",
                columns: new[] { "media_session_id", "identity" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_participants_team_id",
                table: "live_solo_media_participants",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_participants_user_id",
                table: "live_solo_media_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_sessions_match_id_generation",
                table: "live_solo_media_sessions",
                columns: new[] { "match_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_sessions_room_identity",
                table: "live_solo_media_sessions",
                column: "room_identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_pause_intervals_round_id_started_at",
                table: "live_solo_pause_intervals",
                columns: new[] { "round_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_file_id",
                table: "live_solo_program_segments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_media_session_id_sequence",
                table: "live_solo_program_segments",
                columns: new[] { "media_session_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_remove_after",
                table: "live_solo_program_segments",
                column: "remove_after");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_exposures_match_id",
                table: "live_solo_question_exposures",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_group_items_competition_challenge_id",
                table: "live_solo_question_group_items",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_group_items_question_group_id_competitio",
                table: "live_solo_question_group_items",
                columns: new[] { "question_group_id", "competition_challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_groups_competition_id_position",
                table: "live_solo_question_groups",
                columns: new[] { "competition_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_file_id",
                table: "live_solo_recordings",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id",
                table: "live_solo_recordings",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_round_id",
                table: "live_solo_recordings",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_state_keep_until",
                table: "live_solo_recordings",
                columns: new[] { "state", "keep_until" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_user_id",
                table: "live_solo_recordings",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_roster_members_team_id",
                table: "live_solo_roster_members",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_roster_members_user_id",
                table: "live_solo_roster_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_questions_competition_challenge_id",
                table: "live_solo_round_questions",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_questions_round_id_competition_challenge_id",
                table: "live_solo_round_questions",
                columns: new[] { "round_id", "competition_challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_questions_round_id_position",
                table: "live_solo_round_questions",
                columns: new[] { "round_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_match_id_number_replay",
                table: "live_solo_rounds",
                columns: new[] { "match_id", "number", "replay" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_question_group_id",
                table: "live_solo_rounds",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_winner_team_id",
                table: "live_solo_rounds",
                column: "winner_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_winning_gameplay_fact_id",
                table: "live_solo_rounds",
                column: "winning_gameplay_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_runtime_bindings_runtime_instance_id",
                table: "live_solo_runtime_bindings",
                column: "runtime_instance_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_submissions_round_id_admission_sequence",
                table: "live_solo_submissions",
                columns: new[] { "round_id", "admission_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_submissions_round_question_id",
                table: "live_solo_submissions",
                column: "round_question_id");

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_active_team_slots_live_solo_matches_match_id",
                table: "live_solo_active_team_slots",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_download_evidence_live_solo_round_questions_round",
                table: "live_solo_download_evidence",
                column: "round_question_id",
                principalTable: "live_solo_round_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_match_slots_live_solo_matches_match_id",
                table: "live_solo_match_slots",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_match_slots_live_solo_matches_source_match_id",
                table: "live_solo_match_slots",
                column: "source_match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_rounds_current_round_id",
                table: "live_solo_matches",
                column: "current_round_id",
                principalTable: "live_solo_rounds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_live_solo_matches_match_id",
                table: "live_solo_rounds");

            migrationBuilder.DropTable(
                name: "live_solo_active_team_slots");

            migrationBuilder.DropTable(
                name: "live_solo_challenge_sources");

            migrationBuilder.DropTable(
                name: "live_solo_download_evidence");

            migrationBuilder.DropTable(
                name: "live_solo_match_slots");

            migrationBuilder.DropTable(
                name: "live_solo_media_participants");

            migrationBuilder.DropTable(
                name: "live_solo_pause_intervals");

            migrationBuilder.DropTable(
                name: "live_solo_program_segments");

            migrationBuilder.DropTable(
                name: "live_solo_question_exposures");

            migrationBuilder.DropTable(
                name: "live_solo_question_group_items");

            migrationBuilder.DropTable(
                name: "live_solo_recordings");

            migrationBuilder.DropTable(
                name: "live_solo_roster_members");

            migrationBuilder.DropTable(
                name: "live_solo_runtime_bindings");

            migrationBuilder.DropTable(
                name: "live_solo_stage_rules");

            migrationBuilder.DropTable(
                name: "live_solo_submissions");

            migrationBuilder.DropTable(
                name: "live_solo_media_sessions");

            migrationBuilder.DropTable(
                name: "live_solo_round_questions");

            migrationBuilder.DropTable(
                name: "live_solo_matches");

            migrationBuilder.DropTable(
                name: "live_solo_rounds");

            migrationBuilder.DropTable(
                name: "live_solo_question_groups");

            migrationBuilder.DropColumn(
                name: "execution_scope_id",
                table: "runtime_instances");

            migrationBuilder.DropColumn(
                name: "bracket_format",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "countdown_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "enabled",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_concurrent_matches",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_roster_members",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "maximum_viewers",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "participants_may_view_opponents",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "public_delay_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "question_interval_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "recording_enabled",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "recording_retention_days",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "required_wins",
                table: "competition_mode_configurations");

            migrationBuilder.DropColumn(
                name: "round_limit_seconds",
                table: "competition_mode_configurations");

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competitions",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_mode_configurations",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_challenges",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "competition_challenge_rules",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "challenges",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);

            migrationBuilder.AlterColumn<string>(
                name: "mode",
                table: "challenge_definitions",
                type: "character varying(4)",
                maxLength: 4,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(8)",
                oldMaxLength: 8);
        }
    }
}

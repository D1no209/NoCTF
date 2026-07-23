using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "durable_maintenance_schedules",
                columns: table => new
                {
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    processing_version = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_durable_maintenance_schedules", x => x.kind);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenges", x => x.id);
                    table.CheckConstraint("ck_challenges_owner_not_manager", "NOT (owner_id = ANY(manager_ids))");
                    table.ForeignKey(
                        name: "fk_challenges_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    judge_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    observer_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    configuration_revision = table.Column<int>(type: "integer", nullable: false),
                    configuration_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    leaderboard_revision = table.Column<long>(type: "bigint", nullable: false),
                    flag_derivation_secret = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    running_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accumulated_running_seconds = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    team_registration_auto_approve = table.Column<bool>(type: "boolean", nullable: false),
                    max_team_members = table.Column<int>(type: "integer", nullable: false),
                    max_concurrent_runtime_instances_per_team = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competitions", x => x.id);
                    table.CheckConstraint("ck_competitions_flag_secret_length", "octet_length(flag_derivation_secret) = 32");
                    table.CheckConstraint("ck_competitions_owner_not_permission", "NOT (owner_id = ANY(manager_ids)) AND NOT (owner_id = ANY(judge_ids)) AND NOT (owner_id = ANY(observer_ids))");
                    table.CheckConstraint("ck_competitions_permission_roles_exclusive", "NOT (manager_ids && judge_ids) AND NOT (manager_ids && observer_ids) AND NOT (judge_ids && observer_ids)");
                    table.CheckConstraint("ck_competitions_schedule", "start_at < end_at");
                    table.ForeignKey(
                        name: "fk_competitions_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_verification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_challenge_attachments_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    base_score = table.Column<long>(type: "bigint", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenges_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenges_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_lifecycle_audits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from = table.Column<short>(type: "smallint", nullable: false),
                    to = table.Column<short>(type: "smallint", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_lifecycle_audits", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_lifecycle_audits_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    captain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    invitation_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    registration_status = table.Column<short>(type: "smallint", nullable: false),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_banned = table.Column<bool>(type: "boolean", nullable: false),
                    banned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    banned_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ban_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.CheckConstraint("ck_teams_captain_is_member", "captain_id = ANY(member_ids)");
                    table.CheckConstraint("ck_teams_invitation_token_length", "char_length(invitation_token) = 32");
                    table.CheckConstraint("ck_teams_members_not_empty", "cardinality(member_ids) > 0");
                    table.ForeignKey(
                        name: "fk_teams_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teams_users_captain_id",
                        column: x => x.captain_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_hints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    cost = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_hints", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_hints_competition_challenges_competit",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flag_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    specification_kind = table.Column<short>(type: "smallint", nullable: true),
                    specification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flag = table.Column<string>(type: "text", nullable: false),
                    valid_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    valid_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_flags", x => x.id);
                    table.CheckConstraint("ck_challenge_flags_scope", "(challenge_id IS NULL) <> (competition_challenge_id IS NULL)");
                    table.CheckConstraint("ck_challenge_flags_specification", "(specification_kind IS NULL) = (specification_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_challenge_flags_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_challenge_flags_competition_challenges_competition_challeng",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_challenge_flags_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patch_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patch_uploads", x => x.id);
                    table.CheckConstraint("ck_patch_uploads_consumption", "(consumed_at IS NULL) = (submission_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_patch_uploads_competition_challenges_competition_challenge_",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patch_uploads_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patch_uploads_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patch_uploads_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "runtime_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    generation = table.Column<int>(type: "integer", nullable: false),
                    runtime_kind = table.Column<short>(type: "smallint", nullable: false),
                    runtime_provider = table.Column<short>(type: "smallint", nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    runner_pool = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    runner_assignment_release_token = table.Column<Guid>(type: "uuid", nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    runner_unavailable_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_version = table.Column<long>(type: "bigint", nullable: false),
                    configuration_revision = table.Column<int>(type: "integer", nullable: false),
                    replaces_runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_receipt_json = table.Column<string>(type: "jsonb", nullable: true),
                    urls = table.Column<string[]>(type: "text[]", nullable: false),
                    participant_url_indexes = table.Column<int[]>(type: "integer[]", nullable: false),
                    control_check_url = table.Column<string>(type: "text", nullable: true),
                    checker_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_applied_checker_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_applied_checker_body_sha256 = table.Column<byte[]>(type: "bytea", nullable: true),
                    next_checker_due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    running_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_instances", x => x.id);
                    table.CheckConstraint("ck_runtime_instances_checker_sequence", "last_applied_checker_sequence <= checker_sequence");
                    table.CheckConstraint("ck_runtime_instances_failure", "(state = 5) = (failure_code IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_runtime_instances_competition_challenges_competition_challe",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runtime_instances_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runtime_instances_runtime_instances_replaces_runtime_instan",
                        column: x => x.replaces_runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_runtime_instances_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scoring_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    victim_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    result = table.Column<short>(type: "smallint", nullable: false),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    specification_kind = table.Column<short>(type: "smallint", nullable: true),
                    specification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_version = table.Column<long>(type: "bigint", nullable: true),
                    competition_configuration_revision = table.Column<int>(type: "integer", nullable: false),
                    competition_challenge_revision = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scoring_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_scoring_events_competition_challenges_competition_challenge",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scoring_events_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scoring_events_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scoring_events_teams_victim_team_id",
                        column: x => x.victim_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_flag = table.Column<string>(type: "text", nullable: true),
                    submitted_flag_sha256 = table.Column<byte[]>(type: "bytea", nullable: true),
                    patch_upload_id = table.Column<Guid>(type: "uuid", nullable: true),
                    evaluation_state = table.Column<short>(type: "smallint", nullable: false),
                    evaluation_failure_code = table.Column<short>(type: "smallint", nullable: true),
                    evaluation_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_scoring_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    processing_version = table.Column<long>(type: "bigint", nullable: false),
                    evaluation_result_body_sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_submissions", x => x.id);
                    table.CheckConstraint("ck_submissions_payload", "(kind IN (0, 1) AND submitted_flag IS NOT NULL AND submitted_flag_sha256 IS NOT NULL AND patch_upload_id IS NULL) OR (kind = 2 AND submitted_flag IS NULL AND submitted_flag_sha256 IS NULL AND patch_upload_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_submissions_competition_challenges_competition_challenge_id",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_patch_uploads_patch_upload_id",
                        column: x => x.patch_upload_id,
                        principalTable: "patch_uploads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_scoring_events_current_scoring_event_id",
                        column: x => x.current_scoring_event_id,
                        principalTable: "scoring_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_submissions_users_submitted_by_user_id",
                        column: x => x.submitted_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "durable_maintenance_schedules",
                columns: new[] { "kind", "processing_version", "updated_at" },
                values: new object[,]
                {
                    { (short)0, 1L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { (short)1, 1L, new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_challenge_id",
                table: "challenge_attachments",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_object_key",
                table: "challenge_attachments",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_challenge_id_flag_sha256",
                table: "challenge_flags",
                columns: new[] { "challenge_id", "flag_sha256" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_competition_challenge_id_flag_sha256",
                table: "challenge_flags",
                columns: new[] { "competition_challenge_id", "flag_sha256" },
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_competition_challenge_id_team_id_specificat",
                table: "challenge_flags",
                columns: new[] { "competition_challenge_id", "team_id", "specification_kind", "specification_id" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_team_id",
                table: "challenge_flags",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_manager_ids",
                table: "challenges",
                column: "manager_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_owner_id",
                table: "challenges",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_hints_competition_challenge_id",
                table: "competition_challenge_hints",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_challenge_id",
                table: "competition_challenges",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_challenge_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "challenge_id" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_order",
                table: "competition_challenges",
                columns: new[] { "competition_id", "order" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_competition_lifecycle_audits_competition_id_occurred_at",
                table: "competition_lifecycle_audits",
                columns: new[] { "competition_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_judge_ids",
                table: "competitions",
                column: "judge_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_manager_ids",
                table: "competitions",
                column: "manager_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_observer_ids",
                table: "competitions",
                column: "observer_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_owner_id",
                table: "competitions",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_status_start_at",
                table: "competitions",
                columns: new[] { "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_token_sha256",
                table: "email_verification_tokens",
                column: "token_sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_email_verification_tokens_user_id",
                table: "email_verification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_competition_id",
                table: "notifications",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at_id",
                table: "notifications",
                columns: new[] { "user_id", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_competition_challenge_id",
                table: "patch_uploads",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_competition_id",
                table: "patch_uploads",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_object_key",
                table: "patch_uploads",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_submission_id",
                table: "patch_uploads",
                column: "submission_id",
                unique: true,
                filter: "submission_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_team_id_competition_challenge_id",
                table: "patch_uploads",
                columns: new[] { "team_id", "competition_challenge_id" },
                unique: true,
                filter: "consumed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_uploaded_by_user_id",
                table: "patch_uploads",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "state IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_generati",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id", "generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_id",
                table: "runtime_instances",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_replaces_runtime_instance_id",
                table: "runtime_instances",
                column: "replaces_runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_runner_pool_state_created_at_id",
                table: "runtime_instances",
                columns: new[] { "runner_pool", "state", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_state_next_checker_due_at",
                table: "runtime_instances",
                columns: new[] { "state", "next_checker_due_at" },
                filter: "next_checker_due_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_team_id",
                table: "runtime_instances",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_competition_challenge_id",
                table: "scoring_events",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_competition_id_occurred_at_id",
                table: "scoring_events",
                columns: new[] { "competition_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_competition_id_team_id_competition_challenge",
                table: "scoring_events",
                columns: new[] { "competition_id", "team_id", "competition_challenge_id", "kind", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_submission_id",
                table: "scoring_events",
                column: "submission_id",
                unique: true,
                filter: "submission_id IS NOT NULL AND deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_team_id",
                table: "scoring_events",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_events_victim_team_id",
                table: "scoring_events",
                column: "victim_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_competition_challenge_id",
                table: "submissions",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_competition_id_received_at_id",
                table: "submissions",
                columns: new[] { "competition_id", "received_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_competition_id_team_id_competition_challenge_id",
                table: "submissions",
                columns: new[] { "competition_id", "team_id", "competition_challenge_id", "kind", "received_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_submissions_current_scoring_event_id",
                table: "submissions",
                column: "current_scoring_event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_patch_upload_id",
                table: "submissions",
                column: "patch_upload_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_submissions_submitted_by_user_id",
                table: "submissions",
                column: "submitted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_submissions_team_id",
                table: "submissions",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_captain_id",
                table: "teams",
                column: "captain_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_normalized_name",
                table: "teams",
                columns: new[] { "competition_id", "normalized_name" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_registration_status",
                table: "teams",
                columns: new[] { "competition_id", "registration_status" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_invitation_token",
                table: "teams",
                column: "invitation_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_member_ids",
                table: "teams",
                column: "member_ids")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_scoring_events_submissions_submission_id",
                table: "scoring_events",
                column: "submission_id",
                principalTable: "submissions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_competition_challenges_challenges_challenge_id",
                table: "competition_challenges");

            migrationBuilder.DropForeignKey(
                name: "fk_patch_uploads_competition_challenges_competition_challenge_",
                table: "patch_uploads");

            migrationBuilder.DropForeignKey(
                name: "fk_scoring_events_competition_challenges_competition_challenge",
                table: "scoring_events");

            migrationBuilder.DropForeignKey(
                name: "fk_submissions_competition_challenges_competition_challenge_id",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "fk_patch_uploads_teams_team_id",
                table: "patch_uploads");

            migrationBuilder.DropForeignKey(
                name: "fk_scoring_events_teams_team_id",
                table: "scoring_events");

            migrationBuilder.DropForeignKey(
                name: "fk_scoring_events_teams_victim_team_id",
                table: "scoring_events");

            migrationBuilder.DropForeignKey(
                name: "fk_submissions_teams_team_id",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "fk_competitions_users_owner_id",
                table: "competitions");

            migrationBuilder.DropForeignKey(
                name: "fk_patch_uploads_users_uploaded_by_user_id",
                table: "patch_uploads");

            migrationBuilder.DropForeignKey(
                name: "fk_submissions_users_submitted_by_user_id",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "fk_patch_uploads_competitions_competition_id",
                table: "patch_uploads");

            migrationBuilder.DropForeignKey(
                name: "fk_scoring_events_competitions_competition_id",
                table: "scoring_events");

            migrationBuilder.DropForeignKey(
                name: "fk_submissions_competitions_competition_id",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "fk_scoring_events_submissions_submission_id",
                table: "scoring_events");

            migrationBuilder.DropTable(
                name: "challenge_attachments");

            migrationBuilder.DropTable(
                name: "challenge_flags");

            migrationBuilder.DropTable(
                name: "competition_challenge_hints");

            migrationBuilder.DropTable(
                name: "competition_lifecycle_audits");

            migrationBuilder.DropTable(
                name: "durable_maintenance_schedules");

            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "runtime_instances");

            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "competition_challenges");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "competitions");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "patch_uploads");

            migrationBuilder.DropTable(
                name: "scoring_events");
        }
    }
}

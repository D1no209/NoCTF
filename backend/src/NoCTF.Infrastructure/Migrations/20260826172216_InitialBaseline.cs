using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    file_name = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    content_type = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                    table.CheckConstraint("ck_files_byte_length", "byte_length >= 0");
                    table.CheckConstraint("ck_files_sha256_length", "octet_length(sha256) = 32");
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_type = table.Column<short>(type: "smallint", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_type = table.Column<short>(type: "smallint", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    content_json = table.Column<string>(type: "jsonb", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    related_type = table.Column<short>(type: "smallint", nullable: true),
                    related_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thread_root_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_to_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_content", "jsonb_typeof(content_json) = 'object' AND content_json ? 'schemaVersion'");
                    table.CheckConstraint("ck_notifications_platform_administrators_target", "target_type <> 4 OR target_id = 'ffffffff-ffff-ffff-ffff-ffffffffffff'::uuid");
                    table.CheckConstraint("ck_notifications_related_reference", "(related_type IS NULL) = (related_id IS NULL)");
                    table.CheckConstraint("ck_notifications_source", "(source_type IN (0, 4) AND source_id IS NULL) OR (source_type IN (1, 2, 3) AND source_id IS NOT NULL)");
                    table.CheckConstraint("ck_notifications_thread_root", "thread_root_id IS NULL OR thread_root_id <> id");
                    table.ForeignKey(
                        name: "fk_notifications_notifications_reply_to_id",
                        column: x => x.reply_to_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_notifications_thread_root_id",
                        column: x => x.thread_root_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "platform_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email_verification_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    email_public_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    email_verification_token_lifetime_minutes = table.Column<int>(type: "integer", nullable: false),
                    email_verification_resend_cooldown_seconds = table.Column<int>(type: "integer", nullable: false),
                    email_password_reset_token_lifetime_minutes = table.Column<int>(type: "integer", nullable: false),
                    email_password_reset_cooldown_seconds = table.Column<int>(type: "integer", nullable: false),
                    email_password_reset_max_requests_per_hour = table.Column<int>(type: "integer", nullable: false),
                    email_smtp_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    email_smtp_port = table.Column<int>(type: "integer", nullable: false),
                    email_smtp_security_mode = table.Column<short>(type: "smallint", nullable: true),
                    email_smtp_user_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    email_smtp_password_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 2048, nullable: true),
                    email_smtp_from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    email_smtp_from_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    email_smtp_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_settings", x => x.id);
                    table.CheckConstraint("ck_platform_settings_singleton", "id = 1");
                    table.ForeignKey(
                        name: "fk_platform_settings_files_logo_file_id",
                        column: x => x.logo_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    account_status = table.Column<short>(type: "smallint", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    email_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_bot_role", "\"kind\" <> 1 OR \"role\" IN (0, 1)");
                    table.ForeignKey(
                        name: "fk_users_files_avatar_file_id",
                        column: x => x.avatar_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    token_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invalidated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_tokens", x => x.id);
                    table.CheckConstraint("ck_account_tokens_consumed_at", "consumed_at IS NULL OR consumed_at >= created_at");
                    table.CheckConstraint("ck_account_tokens_email_not_invalidated", "kind <> 0 OR invalidated_at IS NULL");
                    table.CheckConstraint("ck_account_tokens_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_account_tokens_hash_length", "octet_length(token_sha256) = 32");
                    table.CheckConstraint("ck_account_tokens_invalidated_at", "invalidated_at IS NULL OR invalidated_at >= created_at");
                    table.CheckConstraint("ck_account_tokens_terminal_state", "consumed_at IS NULL OR invalidated_at IS NULL");
                    table.ForeignKey(
                        name: "fk_account_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
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
                    poster_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    manager_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    judge_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    observer_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    mode = table.Column<short>(type: "smallint", nullable: false),
                    configuration_json = table.Column<string>(type: "jsonb", nullable: false),
                    track_configuration_json = table.Column<string>(type: "jsonb", nullable: true),
                    frozen_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hidden_start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    flag_derivation_secret = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    start_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    team_registration_auto_approve = table.Column<bool>(type: "boolean", nullable: false),
                    allow_team_registration_while_running = table.Column<bool>(type: "boolean", nullable: false),
                    practice_mode_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    max_team_members = table.Column<int>(type: "integer", nullable: false),
                    max_concurrent_runtime_instances_per_team = table.Column<int>(type: "integer", nullable: false),
                    max_active_questions_per_team = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    max_participant_messages_before_handler_reply = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    allow_challenge_owners_to_handle_questions = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                    table.CheckConstraint("ck_competitions_question_limits", "max_active_questions_per_team > 0 AND max_participant_messages_before_handler_reply > 0");
                    table.CheckConstraint("ck_competitions_schedule", "start_at < end_at");
                    table.ForeignKey(
                        name: "fk_competitions_files_poster_file_id",
                        column: x => x.poster_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competitions_users_owner_id",
                        column: x => x.owner_id,
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
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.ForeignKey(
                        name: "fk_challenge_attachments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
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
                    custom_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hints_json = table.Column<string>(type: "jsonb", nullable: true)
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
                name: "competition_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    level = table.Column<short>(type: "smallint", nullable: false),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subject_type = table.Column<short>(type: "smallint", nullable: false),
                    subject_id = table.Column<Guid>(type: "uuid", nullable: false),
                    related_type = table.Column<short>(type: "smallint", nullable: true),
                    related_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_events", x => x.id);
                    table.CheckConstraint("ck_competition_events_payload", "jsonb_typeof(payload_json) = 'object' AND payload_json ? 'schemaVersion'");
                    table.CheckConstraint("ck_competition_events_related_reference", "(related_type IS NULL) = (related_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_competition_events_competition_events_parent_event_id",
                        column: x => x.parent_event_id,
                        principalTable: "competition_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_events_users_actor_user_id",
                        column: x => x.actor_user_id,
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
                    track_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_teams_files_avatar_file_id",
                        column: x => x.avatar_file_id,
                        principalTable: "files",
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
                    match_kind = table.Column<short>(type: "smallint", nullable: false),
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
                name: "gameplay_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    victim_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reference_kind = table.Column<short>(type: "smallint", nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true),
                    value_sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gameplay_facts", x => x.id);
                    table.CheckConstraint("ck_gameplay_facts_koh_team", "kind <> 6 OR (result = 9 AND team_id IS NOT NULL) OR (result IS DISTINCT FROM 9 AND team_id IS NULL)");
                    table.CheckConstraint("ck_gameplay_facts_reference", "(reference_kind IS NULL) = (reference_id IS NULL)");
                    table.CheckConstraint("ck_gameplay_facts_result", "result IS NULL OR (kind IN (0, 1) AND result IN (0, 1, 2, 3, 4)) OR (kind = 2 AND result IN (0, 1, 3, 4)) OR (kind = 3 AND result IN (2, 4, 5)) OR (kind = 4 AND result = 6) OR (kind = 5 AND result IN (7, 8)) OR (kind = 6 AND result IN (9, 10))");
                    table.CheckConstraint("ck_gameplay_facts_shape", "(kind = 0 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NOT NULL AND octet_length(value_sha256) = 32 AND (reference_kind IS NULL OR reference_kind = 2)) OR (kind = 1 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NOT NULL AND octet_length(value_sha256) = 32 AND reference_kind IS NULL) OR (kind = 2 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind = 0) OR (kind = 3 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind = 1) OR (kind = 4 AND team_id IS NOT NULL AND actor_user_id IS NOT NULL AND value ~ '^-?(0|[1-9][0-9]*)$' AND value <> '0' AND value <> '-0' AND value::bigint BETWEEN -2147483648 AND 2147483647 AND value_sha256 IS NULL AND reference_kind IS NULL) OR (kind = 5 AND team_id IS NOT NULL AND actor_user_id IS NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind IS NULL AND victim_team_id IS NULL) OR (kind = 6 AND actor_user_id IS NULL AND value IS NULL AND value_sha256 IS NULL AND reference_kind IS NULL AND victim_team_id IS NULL)");
                    table.CheckConstraint("ck_gameplay_facts_state_result", "state <> 3 OR result IS NOT NULL");
                    table.CheckConstraint("ck_gameplay_facts_victim", "victim_team_id IS NULL OR kind IN (0, 1)");
                    table.ForeignKey(
                        name: "fk_gameplay_facts_competition_challenges_competition_challenge",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gameplay_facts_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gameplay_facts_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gameplay_facts_teams_victim_team_id",
                        column: x => x.victim_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_gameplay_facts_users_actor_user_id",
                        column: x => x.actor_user_id,
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
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_kind = table.Column<short>(type: "smallint", nullable: false),
                    runtime_provider = table.Column<short>(type: "smallint", nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    provider_receipt_json = table.Column<string>(type: "jsonb", nullable: true),
                    urls = table.Column<string[]>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    running_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    stopped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_ports_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_instances", x => x.id);
                    table.UniqueConstraint("ak_runtime_instances_id_competition_id", x => new { x.id, x.competition_id });
                    table.CheckConstraint("ck_runtime_instances_awdp_gameplay_fact", "gameplay_fact_id IS NULL OR purpose = 1");
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
                        name: "fk_runtime_instances_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
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
                name: "patch_uploads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patch_uploads", x => x.id);
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
                        name: "fk_patch_uploads_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_patch_uploads_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
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

            migrationBuilder.InsertData(
                table: "platform_settings",
                columns: new[] { "id", "description", "email_password_reset_cooldown_seconds", "email_password_reset_max_requests_per_hour", "email_password_reset_token_lifetime_minutes", "email_public_base_url", "email_smtp_from_address", "email_smtp_from_name", "email_smtp_host", "email_smtp_password_ciphertext", "email_smtp_port", "email_smtp_security_mode", "email_smtp_timeout_seconds", "email_smtp_user_name", "email_verification_enabled", "email_verification_resend_cooldown_seconds", "email_verification_token_lifetime_minutes", "logo_file_id", "name", "updated_at" },
                values: new object[] { (short)1, null, 60, 5, 30, "http://localhost:5000", "", "NoCTF", "", null, 587, null, 30, "", false, 60, 1440, null, "NoCTF", new DateTimeOffset(new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_token_sha256",
                table: "account_tokens",
                column: "token_sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_tokens_user_id_kind_created_at",
                table: "account_tokens",
                columns: new[] { "user_id", "kind", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_challenge_id",
                table: "challenge_attachments",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_file_id",
                table: "challenge_attachments",
                column: "file_id");

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
                name: "ix_challenge_flags_specification_kind_specification_id",
                table: "challenge_flags",
                columns: new[] { "specification_kind", "specification_id" },
                unique: true,
                filter: "specification_kind = 4 AND deleted_at IS NULL");

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
                name: "ix_competition_events_actor_user_id",
                table: "competition_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_kind_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "kind", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_level_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "level", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_related_type_related_id_o",
                table: "competition_events",
                columns: new[] { "competition_id", "related_type", "related_id", "occurred_at", "id" },
                filter: "related_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_subject_type_subject_id_o",
                table: "competition_events",
                columns: new[] { "competition_id", "subject_type", "subject_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_competition_id_visibility_occurred_at_id",
                table: "competition_events",
                columns: new[] { "competition_id", "visibility", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_events_parent_event_id_occurred_at_id",
                table: "competition_events",
                columns: new[] { "parent_event_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_frozen_start_at_hidden_start_at",
                table: "competitions",
                columns: new[] { "frozen_start_at", "hidden_start_at" });

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
                name: "ix_competitions_poster_file_id",
                table: "competitions",
                column: "poster_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_status_start_at",
                table: "competitions",
                columns: new[] { "status", "start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_files_object_key",
                table: "files",
                column: "object_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_actor_user_id",
                table: "gameplay_facts",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_competition_challenge_id",
                table: "gameplay_facts",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_competition_id_occurred_at_id",
                table: "gameplay_facts",
                columns: new[] { "competition_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_competition_id_team_id_competition_challenge",
                table: "gameplay_facts",
                columns: new[] { "competition_id", "team_id", "competition_challenge_id", "kind", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_reference_id",
                table: "gameplay_facts",
                column: "reference_id",
                unique: true,
                filter: "reference_kind = 0");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_team_id",
                table: "gameplay_facts",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_victim_team_id",
                table: "gameplay_facts",
                column: "victim_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_related_type_related_id_sent_at_id",
                table: "notifications",
                columns: new[] { "related_type", "related_id", "sent_at", "id" },
                filter: "related_type IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_reply_to_id",
                table: "notifications",
                column: "reply_to_id",
                filter: "reply_to_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_type_source_id_sent_at_id",
                table: "notifications",
                columns: new[] { "source_type", "source_id", "sent_at", "id" },
                filter: "source_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_target_type_target_id_sent_at_id",
                table: "notifications",
                columns: new[] { "target_type", "target_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_thread_root_id_sent_at_id",
                table: "notifications",
                columns: new[] { "thread_root_id", "sent_at", "id" },
                filter: "thread_root_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_competition_challenge_id",
                table: "patch_uploads",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_competition_id",
                table: "patch_uploads",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_file_id",
                table: "patch_uploads",
                column: "file_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_runtime_instance_id",
                table: "patch_uploads",
                column: "runtime_instance_id",
                unique: true,
                filter: "runtime_instance_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_team_id_competition_challenge_id",
                table: "patch_uploads",
                columns: new[] { "team_id", "competition_challenge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_patch_uploads_uploaded_by_user_id",
                table: "patch_uploads",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_platform_settings_logo_file_id",
                table: "platform_settings",
                column: "logo_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" },
                unique: true,
                filter: "purpose IN (0, 2, 3) AND state IN (0, 1, 2)");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id_purpose_",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id", "purpose", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_id",
                table: "runtime_instances",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_gameplay_fact_id",
                table: "runtime_instances",
                column: "gameplay_fact_id",
                unique: true,
                filter: "purpose = 1 AND gameplay_fact_id IS NOT NULL AND state IN (0, 1, 2, 3)");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_runner_id_state_created_at_id",
                table: "runtime_instances",
                columns: new[] { "runner_id", "state", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_team_id",
                table: "runtime_instances",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_avatar_file_id",
                table: "teams",
                column: "avatar_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_captain_id",
                table: "teams",
                column: "captain_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_registration_status",
                table: "teams",
                columns: new[] { "competition_id", "registration_status" });

            migrationBuilder.CreateIndex(
                name: "ix_teams_competition_id_track_key",
                table: "teams",
                columns: new[] { "competition_id", "track_key" });

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
                name: "ix_users_avatar_file_id",
                table: "users",
                column: "avatar_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "email <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "challenge_attachments");

            migrationBuilder.DropTable(
                name: "challenge_flags");

            migrationBuilder.DropTable(
                name: "competition_events");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "patch_uploads");

            migrationBuilder.DropTable(
                name: "platform_settings");

            migrationBuilder.DropTable(
                name: "runtime_instances");

            migrationBuilder.DropTable(
                name: "gameplay_facts");

            migrationBuilder.DropTable(
                name: "competition_challenges");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "competitions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "files");
        }
    }
}

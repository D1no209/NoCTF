using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NoCTF.Persistence.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class InitialBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "command_receipts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<short>(type: "smallint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    input_fingerprint = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    result_state = table.Column<short>(type: "smallint", nullable: false),
                    primary_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    secondary_result_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_state = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_state = table.Column<short>(type: "smallint", nullable: true),
                    result_occurred_at = table.Column<long>(type: "bigint", nullable: true),
                    committed_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_receipts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    xml = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

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
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
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
                    source_event_key = table.Column<string>(type: "text", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    title = table.Column<string>(type: "text", nullable: true),
                    body = table.Column<string>(type: "text", nullable: true),
                    code = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "text", nullable: true),
                    challenge_title = table.Column<string>(type: "text", nullable: true),
                    team_name = table.Column<string>(type: "text", nullable: true),
                    user_name = table.Column<string>(type: "text", nullable: true),
                    provider_name = table.Column<string>(type: "text", nullable: true),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    challenge_flag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appeal_event_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    jwt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<long>(type: "bigint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: true),
                    action_value = table.Column<int>(type: "integer", nullable: true),
                    state_value = table.Column<int>(type: "integer", nullable: true),
                    previous_state_value = table.Column<int>(type: "integer", nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: true),
                    payload_occurred_at = table.Column<long>(type: "bigint", nullable: true),
                    payload_expires_at = table.Column<long>(type: "bigint", nullable: true),
                    range_from = table.Column<long>(type: "bigint", nullable: true),
                    range_to = table.Column<long>(type: "bigint", nullable: true),
                    question_subject = table.Column<short>(type: "smallint", nullable: true),
                    question_status = table.Column<short>(type: "smallint", nullable: true),
                    previous_question_status = table.Column<short>(type: "smallint", nullable: true),
                    question_actor_role = table.Column<short>(type: "smallint", nullable: true),
                    user_lifecycle_action = table.Column<short>(type: "smallint", nullable: true),
                    sso_protocol = table.Column<short>(type: "smallint", nullable: true),
                    runtime_state = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_state = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_result = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_failure_code = table.Column<short>(type: "smallint", nullable: true),
                    sent_at = table.Column<long>(type: "bigint", nullable: false),
                    related_type = table.Column<short>(type: "smallint", nullable: true),
                    related_id = table.Column<Guid>(type: "uuid", nullable: true),
                    thread_root_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reply_to_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
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
                name: "runtime_capacity_ledger",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_capacity_ledger", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "command_receipt_gameplay_fact_results",
                columns: table => new
                {
                    command_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_command_receipt_gameplay_fact_results", x => new { x.command_receipt_id, x.position });
                    table.ForeignKey(
                        name: "fk_command_receipt_gameplay_fact_results_command_receipts_comm",
                        column: x => x.command_receipt_id,
                        principalTable: "command_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "platform_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    logo_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    human_verification_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    human_verification_runtime_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    human_verification_evaluation_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ctf_patch_verification_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    human_verification_provider = table.Column<short>(type: "smallint", nullable: true),
                    human_verification_cap_server_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    human_verification_cap_site_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    human_verification_cap_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: true),
                    human_verification_turnstile_site_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    human_verification_turnstile_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: true),
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
                    sso_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    sso_public_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_settings", x => x.id);
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
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_user_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    account_status = table.Column<short>(type: "smallint", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    school_full_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    school_student_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    profile_cover_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallpaper_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    wallpaper_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    email_verified_at = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_files_avatar_file_id",
                        column: x => x.avatar_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_files_profile_cover_file_id",
                        column: x => x.profile_cover_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_users_files_wallpaper_file_id",
                        column: x => x.wallpaper_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notification_reference_counts",
                columns: table => new
                {
                    notification_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_kind = table.Column<short>(type: "smallint", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_reference_counts", x => new { x.notification_id, x.reference_kind });
                    table.ForeignKey(
                        name: "fk_notification_reference_counts_notifications_notification_id",
                        column: x => x.notification_id,
                        principalTable: "notifications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "human_verification_turnstile_hostnames",
                columns: table => new
                {
                    platform_settings_id = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    hostname = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_human_verification_turnstile_hostnames", x => new { x.platform_settings_id, x.position });
                    table.ForeignKey(
                        name: "fk_human_verification_turnstile_hostnames_platform_settings_pl",
                        column: x => x.platform_settings_id,
                        principalTable: "platform_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sso_providers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_settings_id = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    icon_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    protocol = table.Column<short>(type: "smallint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    allow_login = table.Column<bool>(type: "boolean", nullable: false),
                    allow_binding = table.Column<bool>(type: "boolean", nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    identity_namespace = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    login_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    service_validate_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    display_name_attribute = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    issuer = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    discovery_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    client_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    client_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    read_user_info = table.Column<bool>(type: "boolean", nullable: true),
                    display_name_claim = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_providers", x => x.id);
                    table.ForeignKey(
                        name: "fk_sso_providers_platform_settings_platform_settings_id",
                        column: x => x.platform_settings_id,
                        principalTable: "platform_settings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    token_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    consumed_at = table.Column<long>(type: "bigint", nullable: true),
                    invalidated_at = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_tokens", x => x.id);
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
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    visibility = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    normalized_direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenges", x => x.id);
                    table.UniqueConstraint("ak_challenges_id_mode", x => new { x.id, x.mode });
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
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    poster_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_mode = table.Column<short>(type: "smallint", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    tracks_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    frozen_start_at = table.Column<long>(type: "bigint", nullable: true),
                    hidden_start_at = table.Column<long>(type: "bigint", nullable: true),
                    flag_derivation_secret = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    start_at = table.Column<long>(type: "bigint", nullable: false),
                    end_at = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<short>(type: "smallint", nullable: false),
                    team_registration_auto_approve = table.Column<bool>(type: "boolean", nullable: false),
                    allow_team_registration_while_running = table.Column<bool>(type: "boolean", nullable: false),
                    practice_mode_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    write_up_submission_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    write_up_submission_deadline_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_team_members = table.Column<int>(type: "integer", nullable: false),
                    max_concurrent_runtime_instances_per_team = table.Column<int>(type: "integer", nullable: false),
                    runtime_access_mode = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)0),
                    traffic_capture_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    traffic_capture_limit_bytes = table.Column<long>(type: "bigint", nullable: true),
                    max_active_questions_per_team = table.Column<int>(type: "integer", nullable: false, defaultValue: 5),
                    max_participant_messages_before_handler_reply = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    allow_challenge_owners_to_handle_questions = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competitions", x => x.id);
                    table.UniqueConstraint("ak_competitions_id_mode", x => new { x.id, x.mode });
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
                name: "external_identities",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protocol = table.Column<short>(type: "smallint", nullable: false),
                    identity_namespace = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    normalized_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    bound_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_identities", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_external_identities_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sso_provider_allowed_hosts",
                columns: table => new
                {
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_provider_allowed_hosts", x => new { x.sso_provider_id, x.position });
                    table.ForeignKey(
                        name: "fk_sso_provider_allowed_hosts_sso_providers_sso_provider_id",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sso_provider_oidc_scopes",
                columns: table => new
                {
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_provider_oidc_scopes", x => new { x.sso_provider_id, x.position });
                    table.ForeignKey(
                        name: "fk_sso_provider_oidc_scopes_sso_provider_configuration_sso_pro",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
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
                name: "challenge_definitions",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    patch_entrypoint = table.Column<string>(type: "text", nullable: true),
                    patch_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    ready_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    maximum_patch_upload_bytes = table.Column<long>(type: "bigint", nullable: true),
                    checker_fix_input = table.Column<bool>(type: "boolean", nullable: false),
                    checker_allow_root = table.Column<bool>(type: "boolean", nullable: false),
                    has_flag_template = table.Column<bool>(type: "boolean", nullable: false),
                    flag_template_body_template = table.Column<string>(type: "text", nullable: false),
                    flag_template_header = table.Column<string>(type: "text", nullable: false),
                    flag_template_leet_literal_text = table.Column<bool>(type: "boolean", nullable: false),
                    flag_injection_command = table.Column<string>(type: "text", nullable: true),
                    flag_injection_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    flag_injection_service_name = table.Column<string>(type: "text", nullable: true),
                    interaction_kind = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_definitions", x => x.challenge_id);
                    table.ForeignKey(
                        name: "fk_challenge_definitions_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_managers",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_managers", x => new { x.challenge_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_challenge_managers_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    custom_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    normalized_custom_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenges_challenges_challenge_id_mode",
                        columns: x => new { x.challenge_id, x.mode },
                        principalTable: "challenges",
                        principalColumns: new[] { "id", "mode" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenges_competitions_competition_id_mode",
                        columns: x => new { x.competition_id, x.mode },
                        principalTable: "competitions",
                        principalColumns: new[] { "id", "mode" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_collaborators",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_collaborators", x => new { x.competition_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_competition_collaborators_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    related_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hint_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    question_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_status = table.Column<short>(type: "smallint", nullable: true),
                    previous_competition_status = table.Column<short>(type: "smallint", nullable: true),
                    leaderboard_visibility = table.Column<short>(type: "smallint", nullable: true),
                    previous_leaderboard_visibility = table.Column<short>(type: "smallint", nullable: true),
                    competition_access_mode = table.Column<short>(type: "smallint", nullable: true),
                    previous_competition_access_mode = table.Column<short>(type: "smallint", nullable: true),
                    competition_audience_change_kind = table.Column<short>(type: "smallint", nullable: true),
                    team_registration_status = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_kind = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_state = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_result = table.Column<short>(type: "smallint", nullable: true),
                    runtime_state = table.Column<short>(type: "smallint", nullable: true),
                    runtime_cleanup_result = table.Column<short>(type: "smallint", nullable: true),
                    question_status = table.Column<short>(type: "smallint", nullable: true),
                    host_port = table.Column<int>(type: "integer", nullable: true),
                    reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    track_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    previous_track_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    automatic = table.Column<bool>(type: "boolean", nullable: false),
                    patch_upload_id = table.Column<Guid>(type: "uuid", nullable: true),
                    awdp_fix_outcome = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_failure_code = table.Column<short>(type: "smallint", nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true),
                    track_configuration_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    default_track_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reassigned_team_count = table.Column<int>(type: "integer", nullable: true),
                    includes_protected_flags = table.Column<bool>(type: "boolean", nullable: true),
                    frozen_start_at = table.Column<long>(type: "bigint", nullable: true),
                    hidden_start_at = table.Column<long>(type: "bigint", nullable: true),
                    traffic_segment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    traffic_binding_index = table.Column<int>(type: "integer", nullable: true),
                    traffic_connection_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    traffic_started_at = table.Column<long>(type: "bigint", nullable: true),
                    traffic_ended_at = table.Column<long>(type: "bigint", nullable: true),
                    traffic_client_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    traffic_client_port = table.Column<int>(type: "integer", nullable: true),
                    traffic_destination_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    traffic_destination_port = table.Column<int>(type: "integer", nullable: true),
                    traffic_client_to_runtime_bytes = table.Column<long>(type: "bigint", nullable: true),
                    traffic_runtime_to_client_bytes = table.Column<long>(type: "bigint", nullable: true),
                    traffic_captured_bytes = table.Column<long>(type: "bigint", nullable: true),
                    traffic_truncated = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_events", x => x.id);
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
                name: "competition_mode_configurations",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    flag_template_body_template = table.Column<string>(type: "text", nullable: false),
                    flag_template_header = table.Column<string>(type: "text", nullable: false),
                    flag_template_leet_literal_text = table.Column<bool>(type: "boolean", nullable: false),
                    hardening_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_competition_mode_configuration_round_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    attack_reward_mode = table.Column<short>(type: "smallint", nullable: true),
                    attack_points = table.Column<long>(type: "bigint", nullable: true),
                    victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: true),
                    checker_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    service_healthy_points = table.Column<long>(type: "bigint", nullable: true),
                    service_unhealthy_penalty = table.Column<long>(type: "bigint", nullable: true),
                    round_duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    flag_wrong_penalty = table.Column<long>(type: "bigint", nullable: true),
                    exploit_succeeded_penalty = table.Column<long>(type: "bigint", nullable: true),
                    service_abnormal_penalty = table.Column<long>(type: "bigint", nullable: true),
                    require_break_before_fix = table.Column<bool>(type: "boolean", nullable: true),
                    max_break_submissions = table.Column<int>(type: "integer", nullable: true),
                    max_fix_submissions = table.Column<int>(type: "integer", nullable: true),
                    evaluation_dispatch_mode = table.Column<short>(type: "smallint", nullable: true),
                    break_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    break_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    break_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    break_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    break_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    fix_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    fix_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    fix_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    fix_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    fix_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    wrong_submission_penalty = table.Column<long>(type: "bigint", nullable: true),
                    default_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    default_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    default_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    default_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    default_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    control_points_per_interval = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_mode_configurations", x => x.competition_id);
                    table.ForeignKey(
                        name: "fk_competition_mode_configurations_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_tracks",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_public_selectable = table.Column<bool>(type: "boolean", nullable: false),
                    is_internal = table.Column<bool>(type: "boolean", nullable: false),
                    earns_score = table.Column<bool>(type: "boolean", nullable: false),
                    earns_blood = table.Column<bool>(type: "boolean", nullable: false),
                    affects_dynamic_challenge_score = table.Column<bool>(type: "boolean", nullable: false),
                    visible_on_leaderboard = table.Column<bool>(type: "boolean", nullable: false),
                    affects_competitive_results = table.Column<bool>(type: "boolean", nullable: false),
                    invitation_code = table.Column<string>(type: "text", nullable: true),
                    required_sso_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_tracks", x => new { x.competition_id, x.key });
                    table.ForeignKey(
                        name: "fk_competition_tracks_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    enabled_at = table.Column<long>(type: "bigint", nullable: true),
                    current_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    previous_secret_valid_until = table.Column<long>(type: "bigint", nullable: true),
                    disabled_reason = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_webhook_targets_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "default"),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    write_up_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    write_up_submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    write_up_submitted_at = table.Column<long>(type: "bigint", nullable: true),
                    captain_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invitation_token = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_locked = table.Column<bool>(type: "boolean", nullable: false),
                    registration_status = table.Column<short>(type: "smallint", nullable: false),
                    registered_at = table.Column<long>(type: "bigint", nullable: false),
                    is_banned = table.Column<bool>(type: "boolean", nullable: false),
                    banned_at = table.Column<long>(type: "bigint", nullable: true),
                    banned_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ban_reason = table.Column<string>(type: "text", nullable: true),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
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
                        name: "fk_teams_files_write_up_file_id",
                        column: x => x.write_up_file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_checker_definitions",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    target_service_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_checker_definitions", x => x.challenge_id);
                    table.ForeignKey(
                        name: "fk_challenge_checker_definitions_challenge_definitions_challen",
                        column: x => x.challenge_id,
                        principalTable: "challenge_definitions",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_definition_string_items",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_definition_string_items", x => new { x.challenge_id, x.kind, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_definition_string_items_challenge_definitions_cha",
                        column: x => x.challenge_id,
                        principalTable: "challenge_definitions",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_templates",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_kind = table.Column<short>(type: "smallint", nullable: false),
                    allocation = table.Column<short>(type: "smallint", nullable: false),
                    has_explicit_limits = table.Column<bool>(type: "boolean", nullable: false),
                    ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    operation_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    flag_source = table.Column<short>(type: "smallint", nullable: false),
                    egress_policy = table.Column<short>(type: "smallint", nullable: false),
                    limits_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    limits_nano_cpus = table.Column<long>(type: "bigint", nullable: false),
                    limits_pids_limit = table.Column<long>(type: "bigint", nullable: false),
                    compose_yaml = table.Column<string>(type: "text", nullable: true),
                    image = table.Column<string>(type: "text", nullable: true),
                    flag_environment_variable_name = table.Column<string>(type: "text", nullable: true),
                    security_no_new_privileges = table.Column<bool>(type: "boolean", nullable: true),
                    security_readonly_rootfs = table.Column<bool>(type: "boolean", nullable: true),
                    security_run_as_non_root = table.Column<bool>(type: "boolean", nullable: true),
                    ova_source_url = table.Column<string>(type: "text", nullable: true),
                    sha256 = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_templates", x => x.challenge_id);
                    table.ForeignKey(
                        name: "fk_challenge_runtime_templates_challenge_definitions_challenge",
                        column: x => x.challenge_id,
                        principalTable: "challenge_definitions",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_hints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    cost = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<long>(type: "bigint", nullable: true),
                    hidden_at = table.Column<long>(type: "bigint", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_hints", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_hints_competition_challenges_competit",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_rules",
                columns: table => new
                {
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mode = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    has_score_curve = table.Column<bool>(type: "boolean", nullable: false),
                    has_break_score_curve = table.Column<bool>(type: "boolean", nullable: false),
                    has_fix_score_curve = table.Column<bool>(type: "boolean", nullable: false),
                    max_flag_attempts = table.Column<int>(type: "integer", nullable: true),
                    max_patch_attempts = table.Column<int>(type: "integer", nullable: true),
                    max_break_submissions = table.Column<int>(type: "integer", nullable: true),
                    max_fix_submissions = table.Column<int>(type: "integer", nullable: true),
                    require_break_before_fix = table.Column<bool>(type: "boolean", nullable: true),
                    wrong_submission_penalty = table.Column<long>(type: "bigint", nullable: true),
                    flag_wrong_penalty = table.Column<long>(type: "bigint", nullable: true),
                    exploit_succeeded_penalty = table.Column<long>(type: "bigint", nullable: true),
                    service_abnormal_penalty = table.Column<long>(type: "bigint", nullable: true),
                    attack_reward_mode = table.Column<short>(type: "smallint", nullable: true),
                    attack_points = table.Column<long>(type: "bigint", nullable: true),
                    victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: true),
                    checker_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    service_healthy_points = table.Column<long>(type: "bigint", nullable: true),
                    service_unhealthy_penalty = table.Column<long>(type: "bigint", nullable: true),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    control_points_per_interval = table.Column<long>(type: "bigint", nullable: true),
                    evaluation_dispatch_mode = table.Column<short>(type: "smallint", nullable: true),
                    has_flag_template = table.Column<bool>(type: "boolean", nullable: false),
                    break_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    break_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: false),
                    break_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: false),
                    break_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: false),
                    break_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: false),
                    fix_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    fix_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: false),
                    fix_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: false),
                    fix_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: false),
                    fix_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: false),
                    flag_template_body_template = table.Column<string>(type: "text", nullable: false),
                    flag_template_header = table.Column<string>(type: "text", nullable: false),
                    flag_template_leet_literal_text = table.Column<bool>(type: "boolean", nullable: false),
                    score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: false),
                    score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: false),
                    score_curve_initial_points = table.Column<long>(type: "bigint", nullable: false),
                    score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_rules", x => x.competition_challenge_id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_rules_competition_challenges_competit",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_event_track_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    competition_event_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_event_track_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_event_track_keys_competition_events_competition",
                        column: x => x.competition_event_id,
                        principalTable: "competition_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_blood_rewards",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    policy = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_blood_rewards", x => new { x.competition_id, x.position });
                    table.ForeignKey(
                        name: "fk_competition_blood_rewards_competition_mode_configurations_c",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<short>(type: "smallint", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    flag_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    specification_kind = table.Column<short>(type: "smallint", nullable: true),
                    specification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    specification_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    flag = table.Column<string>(type: "text", nullable: false),
                    match_kind = table.Column<short>(type: "smallint", nullable: false),
                    valid_start = table.Column<long>(type: "bigint", nullable: true),
                    valid_until = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_flags", x => x.id);
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
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    victim_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    reference_kind = table.Column<short>(type: "smallint", nullable: true),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<string>(type: "text", nullable: true),
                    value_sha256 = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gameplay_facts", x => x.id);
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
                name: "team_members",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("ak_team_member_team_id_user_id", x => new { x.team_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_team_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_capabilities",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    add = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_capabilities", x => new { x.challenge_id, x.add, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_capabilities_challenge_runtime_templates_",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_command_items",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_command_items", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_command_items_challenge_runtime_templates",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_internal_ports",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_internal_ports", x => new { x.challenge_id, x.port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_internal_ports_challenge_runtime_template",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_key_values",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_key_values", x => new { x.challenge_id, x.kind, x.key });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_key_values_challenge_runtime_templates_ch",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_port_mappings",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_port_mappings", x => new { x.challenge_id, x.container_port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_port_mappings_challenge_runtime_templates",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_url_bindings",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_control_check = table.Column<bool>(type: "boolean", nullable: false),
                    url_template = table.Column<string>(type: "text", nullable: false),
                    exposure = table.Column<short>(type: "smallint", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: true),
                    service_name = table.Column<string>(type: "text", nullable: true),
                    vm_id = table.Column<string>(type: "text", nullable: true),
                    guest_port = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_url_bindings", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_url_bindings_challenge_runtime_templates_",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compose_service_resources",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "text", nullable: false),
                    limits_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    limits_nano_cpus = table.Column<long>(type: "bigint", nullable: false),
                    limits_pids_limit = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_compose_service_resources", x => new { x.challenge_id, x.service_name });
                    table.ForeignKey(
                        name: "fk_compose_service_resources_challenge_runtime_templates_chall",
                        column: x => x.challenge_id,
                        principalTable: "challenge_runtime_templates",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_blood_rewards",
                columns: table => new
                {
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    policy = table.Column<short>(type: "smallint", nullable: false),
                    value = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_blood_rewards", x => new { x.competition_challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_competition_challenge_blood_rewards_competition_challenge_r",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenge_rules",
                        principalColumn: "competition_challenge_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_instances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    access_mode = table.Column<short>(type: "smallint", nullable: false),
                    traffic_capture_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    traffic_capture_limit_bytes = table.Column<long>(type: "bigint", nullable: true),
                    traffic_capture_reserved_bytes = table.Column<long>(type: "bigint", nullable: false),
                    test_flag_delivery = table.Column<short>(type: "smallint", nullable: true),
                    test_flag_state = table.Column<short>(type: "smallint", nullable: true),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_kind = table.Column<short>(type: "smallint", nullable: false),
                    runtime_provider = table.Column<short>(type: "smallint", nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    failure_code = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    running_at = table.Column<long>(type: "bigint", nullable: true),
                    expires_at = table.Column<long>(type: "bigint", nullable: true),
                    stopped_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_instances", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_instances_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                name: "team_captains",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_captains", x => x.team_id);
                    table.ForeignKey(
                        name: "fk_team_captains_team_member_team_id_user_id",
                        columns: x => new { x.team_id, x.user_id },
                        principalTable: "team_members",
                        principalColumns: new[] { "team_id", "user_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_captains_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "active_runtime_slots",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_runtime_slots", x => x.key);
                    table.ForeignKey(
                        name: "fk_active_runtime_slots_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
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
                    uploaded_at = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "runtime_access_endpoints",
                columns: table => new
                {
                    binding_index = table.Column<int>(type: "integer", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direct_address = table.Column<string>(type: "text", nullable: true),
                    target_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    target_port = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_access_endpoints", x => new { x.runtime_instance_id, x.binding_index });
                    table.ForeignKey(
                        name: "fk_runtime_access_endpoints_runtime_instances_runtime_instance",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_capacity_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workload_kind = table.Column<short>(type: "smallint", nullable: false),
                    workload_runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resource_domain = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    runner_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    budget_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    budget_nano_cpus = table.Column<long>(type: "bigint", nullable: false),
                    budget_pids_limit = table.Column<long>(type: "bigint", nullable: false),
                    limit_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    limit_nano_cpus = table.Column<long>(type: "bigint", nullable: false),
                    limit_pids_limit = table.Column<long>(type: "bigint", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_capacity_allocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_capacity_allocations_runtime_instances_runtime_inst",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_published_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_published_ports", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_published_ports_runtime_instances_runtime_instance_",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipts",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<short>(type: "smallint", nullable: false),
                    receipt_type = table.Column<string>(type: "character varying(21)", maxLength: 21, nullable: false),
                    project_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    @namespace = table.Column<string>(name: "namespace", type: "character varying(255)", maxLength: 255, nullable: true),
                    compose_runtime_receipt_public_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    compose_runtime_receipt_created_at = table.Column<long>(type: "bigint", nullable: true),
                    resource_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: true),
                    public_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    internal_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    container_runtime_receipt_network_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    network_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    network_cidr = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipts", x => x.runtime_instance_id);
                    table.ForeignKey(
                        name: "fk_runtime_receipts_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_ports", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_ports_runtime_receipts_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_receipts",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_virtual_machines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vm_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    address = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_virtual_machines", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_virtual_machines_runtime_receipts_runtime_i",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_receipts",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "platform_settings",
                columns: new[] { "id", "concurrency_stamp", "ctf_patch_verification_enabled", "description", "email_password_reset_cooldown_seconds", "email_password_reset_max_requests_per_hour", "email_password_reset_token_lifetime_minutes", "email_public_base_url", "email_smtp_from_address", "email_smtp_from_name", "email_smtp_host", "email_smtp_password_ciphertext", "email_smtp_port", "email_smtp_security_mode", "email_smtp_timeout_seconds", "email_smtp_user_name", "email_verification_enabled", "email_verification_resend_cooldown_seconds", "email_verification_token_lifetime_minutes", "human_verification_cap_secret_ciphertext", "human_verification_cap_server_url", "human_verification_cap_site_key", "human_verification_enabled", "human_verification_evaluation_enabled", "human_verification_provider", "human_verification_runtime_enabled", "human_verification_turnstile_secret_ciphertext", "human_verification_turnstile_site_key", "logo_file_id", "name", "sso_enabled", "sso_public_base_url", "updated_at" },
                values: new object[] { (short)1, new Guid("00000000-0000-0000-0000-000000000001"), false, null, 60, 5, 30, "http://localhost:5000", "", "NoCTF", "", null, 587, null, 30, "", false, 60, 1440, null, "", "", true, true, null, true, null, "", null, "NoCTF", false, "", 621355968000000000L });

            migrationBuilder.InsertData(
                table: "runtime_capacity_ledger",
                columns: new[] { "id", "concurrency_stamp" },
                values: new object[] { (short)1, new Guid("00000000-0000-0000-0000-000000000002") });

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
                name: "ix_active_runtime_slots_runtime_instance_id",
                table: "active_runtime_slots",
                column: "runtime_instance_id",
                unique: true);

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
                columns: new[] { "challenge_id", "flag_sha256" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_competition_challenge_id_flag_sha256",
                table: "challenge_flags",
                columns: new[] { "competition_challenge_id", "flag_sha256" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_specification_identity",
                table: "challenge_flags",
                column: "specification_identity",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flags_team_id",
                table: "challenge_flags",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_normalized_direction",
                table: "challenges",
                column: "normalized_direction");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_normalized_title",
                table: "challenges",
                column: "normalized_title");

            migrationBuilder.CreateIndex(
                name: "ix_challenges_owner_id",
                table: "challenges",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_command_receipts_user_id_operation_competition_id_resource_",
                table: "command_receipts",
                columns: new[] { "user_id", "operation", "competition_id", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_hints_competition_challenge_id",
                table: "competition_challenge_hints",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_challenge_id_mode",
                table: "competition_challenges",
                columns: new[] { "challenge_id", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_challenge_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_mode",
                table: "competition_challenges",
                columns: new[] { "competition_id", "mode" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_order",
                table: "competition_challenges",
                columns: new[] { "competition_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_normalized_custom_title",
                table: "competition_challenges",
                column: "normalized_custom_title");

            migrationBuilder.CreateIndex(
                name: "ix_competition_collaborators_competition_id_role",
                table: "competition_collaborators",
                columns: new[] { "competition_id", "role" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_event_track_keys_competition_event_id_position",
                table: "competition_event_track_keys",
                columns: new[] { "competition_event_id", "position" },
                unique: true);

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
                columns: new[] { "competition_id", "related_type", "related_id", "occurred_at", "id" });

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
                name: "ix_competition_tracks_competition_id_position",
                table: "competition_tracks",
                columns: new[] { "competition_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_tracks_required_sso_provider_id",
                table: "competition_tracks",
                column: "required_sso_provider_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_targets_competition_id",
                table: "competition_webhook_targets",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competitions_frozen_start_at_hidden_start_at",
                table: "competitions",
                columns: new[] { "frozen_start_at", "hidden_start_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competitions_normalized_title",
                table: "competitions",
                column: "normalized_title");

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
                name: "ix_external_identities_provider_id_normalized_subject",
                table: "external_identities",
                columns: new[] { "provider_id", "normalized_subject" },
                unique: true);

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
                name: "ix_gameplay_facts_reference_kind_reference_id",
                table: "gameplay_facts",
                columns: new[] { "reference_kind", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_team_id",
                table: "gameplay_facts",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_gameplay_facts_victim_team_id",
                table: "gameplay_facts",
                column: "victim_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_human_verification_turnstile_hostnames_platform_settings_id",
                table: "human_verification_turnstile_hostnames",
                columns: new[] { "platform_settings_id", "hostname" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_related_type_related_id_sent_at_id",
                table: "notifications",
                columns: new[] { "related_type", "related_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_reply_to_id",
                table: "notifications",
                column: "reply_to_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_source_type_source_id_sent_at_id",
                table: "notifications",
                columns: new[] { "source_type", "source_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_target_type_target_id_sent_at_id",
                table: "notifications",
                columns: new[] { "target_type", "target_id", "sent_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_thread_root_id_sent_at_id",
                table: "notifications",
                columns: new[] { "thread_root_id", "sent_at", "id" });

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
                unique: true);

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
                name: "ix_runtime_capacity_allocations_runtime_instance_id",
                table: "runtime_capacity_allocations",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_capacity_allocations_workload_kind_workload_runtime",
                table: "runtime_capacity_allocations",
                columns: new[] { "workload_kind", "workload_runtime_instance_id", "operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_challenge_id",
                table: "runtime_instances",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_competition_challenge_id_team_id",
                table: "runtime_instances",
                columns: new[] { "competition_challenge_id", "team_id" });

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
                column: "gameplay_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_runner_id_state_created_at_id",
                table: "runtime_instances",
                columns: new[] { "runner_id", "state", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_runtime_instances_team_id",
                table: "runtime_instances",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_published_ports_runtime_instance_id",
                table: "runtime_published_ports",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_ports_runtime_instance_id",
                table: "runtime_receipt_ports",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_virtual_machines_runtime_instance_id",
                table: "runtime_receipt_virtual_machines",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_sso_providers_platform_settings_id",
                table: "sso_providers",
                column: "platform_settings_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_captains_team_id_user_id",
                table: "team_captains",
                columns: new[] { "team_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_competition_id_user_id",
                table: "team_members",
                columns: new[] { "competition_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_team_members_user_id",
                table: "team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_avatar_file_id",
                table: "teams",
                column: "avatar_file_id");

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
                name: "ix_teams_normalized_name",
                table: "teams",
                column: "normalized_name");

            migrationBuilder.CreateIndex(
                name: "ix_teams_write_up_file_id",
                table: "teams",
                column: "write_up_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_file_id",
                table: "users",
                column: "avatar_file_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_users_profile_cover_file_id",
                table: "users",
                column: "profile_cover_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_wallpaper_file_id",
                table: "users",
                column: "wallpaper_file_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "active_runtime_slots");

            migrationBuilder.DropTable(
                name: "challenge_attachments");

            migrationBuilder.DropTable(
                name: "challenge_checker_definitions");

            migrationBuilder.DropTable(
                name: "challenge_definition_string_items");

            migrationBuilder.DropTable(
                name: "challenge_flags");

            migrationBuilder.DropTable(
                name: "challenge_managers");

            migrationBuilder.DropTable(
                name: "challenge_runtime_capabilities");

            migrationBuilder.DropTable(
                name: "challenge_runtime_command_items");

            migrationBuilder.DropTable(
                name: "challenge_runtime_internal_ports");

            migrationBuilder.DropTable(
                name: "challenge_runtime_key_values");

            migrationBuilder.DropTable(
                name: "challenge_runtime_port_mappings");

            migrationBuilder.DropTable(
                name: "challenge_runtime_url_bindings");

            migrationBuilder.DropTable(
                name: "command_receipt_gameplay_fact_results");

            migrationBuilder.DropTable(
                name: "competition_blood_rewards");

            migrationBuilder.DropTable(
                name: "competition_challenge_blood_rewards");

            migrationBuilder.DropTable(
                name: "competition_challenge_hints");

            migrationBuilder.DropTable(
                name: "competition_collaborators");

            migrationBuilder.DropTable(
                name: "competition_event_track_keys");

            migrationBuilder.DropTable(
                name: "competition_tracks");

            migrationBuilder.DropTable(
                name: "competition_webhook_targets");

            migrationBuilder.DropTable(
                name: "compose_service_resources");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "external_identities");

            migrationBuilder.DropTable(
                name: "human_verification_turnstile_hostnames");

            migrationBuilder.DropTable(
                name: "notification_reference_counts");

            migrationBuilder.DropTable(
                name: "patch_uploads");

            migrationBuilder.DropTable(
                name: "runtime_access_endpoints");

            migrationBuilder.DropTable(
                name: "runtime_capacity_allocations");

            migrationBuilder.DropTable(
                name: "runtime_capacity_ledger");

            migrationBuilder.DropTable(
                name: "runtime_published_ports");

            migrationBuilder.DropTable(
                name: "runtime_receipt_ports");

            migrationBuilder.DropTable(
                name: "runtime_receipt_virtual_machines");

            migrationBuilder.DropTable(
                name: "sso_provider_allowed_hosts");

            migrationBuilder.DropTable(
                name: "sso_provider_oidc_scopes");

            migrationBuilder.DropTable(
                name: "team_captains");

            migrationBuilder.DropTable(
                name: "command_receipts");

            migrationBuilder.DropTable(
                name: "competition_mode_configurations");

            migrationBuilder.DropTable(
                name: "competition_challenge_rules");

            migrationBuilder.DropTable(
                name: "competition_events");

            migrationBuilder.DropTable(
                name: "challenge_runtime_templates");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "runtime_receipts");

            migrationBuilder.DropTable(
                name: "sso_providers");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "challenge_definitions");

            migrationBuilder.DropTable(
                name: "runtime_instances");

            migrationBuilder.DropTable(
                name: "platform_settings");

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

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
                name: "competition_blood_announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<short>(type: "smallint", nullable: false),
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_version = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    last_event_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_blood_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_deliveries",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    payload_state = table.Column<short>(type: "smallint", nullable: false),
                    domain_event_created_at = table.Column<long>(type: "bigint", nullable: false),
                    outbox_persisted_at = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    next_retry_at = table.Column<long>(type: "bigint", nullable: false),
                    enqueue_lease_until = table.Column<long>(type: "bigint", nullable: true),
                    lease_expires_at = table.Column<long>(type: "bigint", nullable: true),
                    worker_dequeued_at = table.Column<long>(type: "bigint", nullable: true),
                    public_projection_ready_at = table.Column<long>(type: "bigint", nullable: true),
                    captured_at = table.Column<long>(type: "bigint", nullable: true),
                    first_http_attempt_started_at = table.Column<long>(type: "bigint", nullable: true),
                    last_http_attempt_started_at = table.Column<long>(type: "bigint", nullable: true),
                    last_http_attempt_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    projection_retry_count = table.Column<int>(type: "integer", nullable: false),
                    http_retry_count = table.Column<int>(type: "integer", nullable: false),
                    last_http_status_code = table.Column<int>(type: "integer", nullable: true),
                    dead_letter_reason = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_deliveries", x => new { x.event_id, x.target_id });
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_frozen_projections",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frozen_at = table.Column<long>(type: "bigint", nullable: false),
                    captured_at = table.Column<long>(type: "bigint", nullable: false),
                    source_event_sequence_through = table.Column<long>(type: "bigint", nullable: false),
                    payload = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_frozen_projections", x => new { x.competition_id, x.frozen_at });
                });

            migrationBuilder.CreateTable(
                name: "competition_webhook_outbox_events",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    domain_event_created_at = table.Column<long>(type: "bigint", nullable: false),
                    outbox_persisted_at = table.Column<long>(type: "bigint", nullable: false),
                    next_dispatch_at = table.Column<long>(type: "bigint", nullable: false),
                    worker_dequeued_at = table.Column<long>(type: "bigint", nullable: true),
                    dispatch_completed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_webhook_outbox_events", x => x.sequence);
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
                    live_solo_match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    live_solo_media_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    live_solo_media_alert_kind = table.Column<short>(type: "smallint", nullable: true),
                    live_solo_screen_state = table.Column<short>(type: "smallint", nullable: true),
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
                name: "questionnaire_sources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    first_exposed_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_sources", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "request_admission_leases",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_admission_leases", x => new { x.key_hash, x.lease_id });
                });

            migrationBuilder.CreateTable(
                name: "request_admission_windows",
                columns: table => new
                {
                    key_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_admission_windows", x => x.key_hash);
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
                name: "sso_flows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protocol = table.Column<short>(type: "smallint", nullable: false),
                    intent = table.Column<short>(type: "smallint", nullable: false),
                    browser_id_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    correlation_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_fingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_version = table.Column<int>(type: "integer", nullable: true),
                    nonce = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    pkce_verifier = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    service_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    processing_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    external_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    external_protocol = table.Column<short>(type: "smallint", nullable: true),
                    external_namespace = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    external_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    external_display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    failure_code = table.Column<int>(type: "integer", nullable: true),
                    authenticated_at = table.Column<long>(type: "bigint", nullable: true),
                    external_mfa_authenticated_at = table.Column<long>(type: "bigint", nullable: true),
                    external_mfa_trust_policy_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sso_flows", x => x.id);
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
                    live_solo_video_maximum_width = table.Column<int>(type: "integer", nullable: false, defaultValue: 1280),
                    live_solo_video_maximum_height = table.Column<int>(type: "integer", nullable: false, defaultValue: 720),
                    live_solo_video_maximum_frames_per_second = table.Column<int>(type: "integer", nullable: false, defaultValue: 10),
                    live_solo_video_maximum_bitrate_bits_per_second = table.Column<int>(type: "integer", nullable: false, defaultValue: 1000000),
                    live_solo_video_policy_stamp = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000005")),
                    human_verification_provider = table.Column<short>(type: "smallint", nullable: true),
                    human_verification_cap_server_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    human_verification_cap_site_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    human_verification_cap_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: true),
                    human_verification_turnstile_site_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    human_verification_turnstile_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: true),
                    email_verification_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_policy = table.Column<short>(type: "smallint", nullable: false),
                    mfa_policy_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    public_base_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
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
                    mfa_required = table.Column<bool>(type: "boolean", nullable: false),
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
                    display_name_claim = table.Column<string>(type: "text", nullable: true),
                    mfa_trust_enabled = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    mfa_trust_policy_id = table.Column<Guid>(type: "uuid", nullable: true, defaultValue: new Guid("00000000-0000-0000-0000-000000000000")),
                    mfa_authentication_max_age_seconds = table.Column<int>(type: "integer", nullable: true, defaultValue: 300)
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
                    catalog_scope = table.Column<short>(type: "smallint", nullable: false),
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
                    scoreboard_access_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    normalized_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    poster_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_mode = table.Column<short>(type: "smallint", nullable: false),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
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
                    single_write_ups_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    single_write_up_deduction_percent = table.Column<int>(type: "integer", nullable: false, defaultValue: 20),
                    single_write_up_deadline_hours = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
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
                name: "mfa_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    operation = table.Column<short>(type: "smallint", nullable: true),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_resource_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    browser_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    policy_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: true),
                    primary_method = table.Column<short>(type: "smallint", nullable: false),
                    primary_authenticated_at = table.Column<long>(type: "bigint", nullable: false),
                    primary_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_provider_fingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    recovery_grant_sha256 = table.Column<byte[]>(type: "bytea", nullable: true),
                    recovery_grant_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: true),
                    mail_state = table.Column<short>(type: "smallint", nullable: false),
                    mail_attempted_at = table.Column<long>(type: "bigint", nullable: true),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    reason = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mfa_challenges", x => x.id);
                    table.ForeignKey(
                        name: "fk_mfa_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "passkey_ceremonies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    token_version = table.Column<int>(type: "integer", nullable: true),
                    local_proof_satisfied = table.Column<bool>(type: "boolean", nullable: false),
                    mfa_policy_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    browser_binding_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    configuration_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    protected_protocol_state = table.Column<byte[]>(type: "bytea", maxLength: 16384, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false),
                    return_path = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_passkey_ceremonies", x => x.id);
                    table.ForeignKey(
                        name: "fk_passkey_ceremonies_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    opens_at = table.Column<long>(type: "bigint", nullable: false),
                    closes_at = table.Column<long>(type: "bigint", nullable: false),
                    published_at = table.Column<long>(type: "bigint", nullable: true),
                    finished_at = table.Column<long>(type: "bigint", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    attempt_limit = table.Column<int>(type: "integer", nullable: true),
                    scoring_mode = table.Column<short>(type: "smallint", nullable: false),
                    issuance_stopped = table.Column<bool>(type: "boolean", nullable: false),
                    allow_exposed_questions = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    selection_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_activities_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_mfa_recovery_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    consumed_at = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_mfa_recovery_codes", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_mfa_recovery_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_passkeys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    credential_id = table.Column<byte[]>(type: "bytea", maxLength: 1023, nullable: false),
                    public_key = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    relying_party_id = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    sign_count = table.Column<long>(type: "bigint", nullable: false),
                    is_user_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_backup_eligible = table.Column<bool>(type: "boolean", nullable: false),
                    is_backed_up = table.Column<bool>(type: "boolean", nullable: false),
                    attestation_object = table.Column<byte[]>(type: "bytea", maxLength: 16384, nullable: false),
                    client_data_json = table.Column<byte[]>(type: "bytea", maxLength: 4096, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    last_used_at = table.Column<long>(type: "bigint", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkeys", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_passkeys_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_totp_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    enabled_at = table.Column<long>(type: "bigint", nullable: false),
                    last_accepted_step = table.Column<long>(type: "bigint", nullable: false),
                    recovery_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_totp_credentials", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_totp_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_acr_values",
                columns: table => new
                {
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_acr_values", x => new { x.sso_provider_id, x.position });
                    table.ForeignKey(
                        name: "fk_oidc_mfa_acr_values_sso_provider_configuration_sso_provider",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_amr_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sso_provider_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_amr_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_oidc_mfa_amr_groups_sso_provider_configuration_sso_provider",
                        column: x => x.sso_provider_id,
                        principalTable: "sso_providers",
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
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                name: "challenge_sources",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    canonical_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    copied_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_sources", x => x.challenge_id);
                    table.ForeignKey(
                        name: "fk_challenge_sources_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_revisions", x => x.id);
                    table.UniqueConstraint("ak_questionnaire_revision_id_challenge_id", x => new { x.id, x.challenge_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_revisions_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_flag_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    protected_secret = table.Column<byte[]>(type: "bytea", maxLength: 256, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_flag_keys", x => x.id);
                    table.UniqueConstraint("ak_awd_flag_keys_competition_id_version", x => new { x.competition_id, x.version });
                    table.ForeignKey(
                        name: "fk_awd_flag_keys_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    submission_tail_seconds = table.Column<int>(type: "integer", nullable: false),
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
                name: "competition_badges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    image_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_badges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_badges_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_badges_files_image_file_id",
                        column: x => x.image_file_id,
                        principalTable: "files",
                        principalColumn: "id",
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
                name: "competition_directions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    icon = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    template_direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_directions", x => x.id);
                    table.UniqueConstraint("ak_competition_direction_competition_id_id", x => new { x.competition_id, x.id });
                    table.ForeignKey(
                        name: "fk_competition_directions_competitions_competition_id",
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
                    traffic_truncated = table.Column<bool>(type: "boolean", nullable: true),
                    awd_rule_timing = table.Column<short>(type: "smallint", nullable: true),
                    awd_effective_from_round = table.Column<int>(type: "integer", nullable: true),
                    awd_effective_through_round = table.Column<int>(type: "integer", nullable: true),
                    awd_adjudication_action = table.Column<short>(type: "smallint", nullable: true),
                    previous_awd_health = table.Column<short>(type: "smallint", nullable: true),
                    awd_health = table.Column<short>(type: "smallint", nullable: true),
                    previous_awd_settlement = table.Column<short>(type: "smallint", nullable: true),
                    awd_settlement = table.Column<short>(type: "smallint", nullable: true)
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
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
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
                    awd_options_attack_settlement = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_attack_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_callback_grace_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_options_composition = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_confidentiality_points = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_consecutive_failure_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_defense_mode = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_defense_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_final_check_budget_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_options_flag_lifetime = table.Column<int>(type: "integer", nullable: true),
                    awd_options_flag_lifetime_unit = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_flags_per_store = table.Column<int>(type: "integer", nullable: true),
                    awd_options_generation_interval = table.Column<int>(type: "integer", nullable: true),
                    awd_options_generation_policy = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_historical_lifetime = table.Column<int>(type: "integer", nullable: true),
                    awd_options_historical_lifetime_unit = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_link_historical_window = table.Column<bool>(type: "boolean", nullable: true),
                    awd_options_maximum_rank_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_maximum_round_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_minimum_attack_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_minimum_total_score = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_mumble_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_mumble_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_mumble_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_offline_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_offline_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_offline_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_ok_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_penalty_mode = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_preset = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_recovering_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_options_recovering_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_recovering_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_retry_count = table.Column<int>(type: "integer", nullable: true),
                    awd_options_retry_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_options_round_gap_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_options_scarcity_alpha = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_sla_basis = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_sla_settlement = table.Column<short>(type: "smallint", nullable: true),
                    awd_options_sla_weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_options_submission_tail_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_options_total_rounds = table.Column<int>(type: "integer", nullable: true),
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
                    score_settlement_mode = table.Column<short>(type: "smallint", nullable: true, defaultValue: (short)0),
                    wrong_submission_penalty = table.Column<long>(type: "bigint", nullable: true),
                    default_score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    default_score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    default_score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    default_score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    default_score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    control_points_per_interval = table.Column<long>(type: "bigint", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: true),
                    static_flag_evidence_required = table.Column<bool>(type: "boolean", nullable: true, defaultValue: false),
                    platform_streaming_enabled = table.Column<bool>(type: "boolean", nullable: true, defaultValue: true),
                    bracket_format = table.Column<short>(type: "smallint", nullable: true),
                    required_wins = table.Column<int>(type: "integer", nullable: true),
                    countdown_seconds = table.Column<int>(type: "integer", nullable: true),
                    question_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    round_limit_seconds = table.Column<int>(type: "integer", nullable: true),
                    public_delay_seconds = table.Column<int>(type: "integer", nullable: true),
                    participants_may_view_opponents = table.Column<bool>(type: "boolean", nullable: true),
                    recording_enabled = table.Column<bool>(type: "boolean", nullable: true),
                    recording_retention_days = table.Column<int>(type: "integer", nullable: true),
                    maximum_concurrent_matches = table.Column<int>(type: "integer", nullable: true),
                    maximum_roster_members = table.Column<int>(type: "integer", nullable: true),
                    maximum_viewers = table.Column<int>(type: "integer", nullable: true)
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
                name: "competition_progressions",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    show_player_map = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progressions", x => x.competition_id);
                    table.ForeignKey(
                        name: "fk_competition_progressions_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_score_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_score_presets", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_score_presets_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_business_sequence = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    competition_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    dispatched_at = table.Column<long>(type: "bigint", nullable: true),
                    change_kind = table.Column<short>(type: "smallint", nullable: true),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    snapshot_reason = table.Column<short>(type: "smallint", nullable: true),
                    as_of_sequence = table.Column<long>(type: "bigint", nullable: true),
                    page_index = table.Column<int>(type: "integer", nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_events_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_streams",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    latest_business_sequence = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_streams", x => x.competition_id);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_streams_competitions_competition_",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_targets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_by_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    endpoint_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    deleted = table.Column<bool>(type: "boolean", nullable: false),
                    authorization_revoked = table.Column<bool>(type: "boolean", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: false),
                    previous_secret_ciphertext = table.Column<byte[]>(type: "bytea", nullable: true),
                    previous_secret_valid_until = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    next_heartbeat_at = table.Column<long>(type: "bigint", nullable: false),
                    failure_since = table.Column<long>(type: "bigint", nullable: true),
                    needs_resync = table.Column<bool>(type: "boolean", nullable: false),
                    active_snapshot_id = table.Column<Guid>(type: "uuid", nullable: true),
                    synced_through = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_targets", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_targets_competitions_competition_",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_targets_users_approved_by_id",
                        column: x => x.approved_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_work_items",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_kind = table.Column<short>(type: "smallint", nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_cheat_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_consultation_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_appeal_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_requires_staff_action = table.Column<bool>(type: "boolean", nullable: false),
                    summary_action_required_since = table.Column<long>(type: "bigint", nullable: true),
                    summary_created_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_updated_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_detected_at = table.Column<long>(type: "bigint", nullable: true),
                    summary_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_related_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_related_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_challenge_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    summary_reason_code = table.Column<short>(type: "smallint", nullable: true),
                    summary_subject = table.Column<short>(type: "smallint", nullable: true),
                    summary_actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_last_changed_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_work_items", x => new { x.competition_id, x.kind, x.item_id });
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_work_items_competitions_competiti",
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
                    table.UniqueConstraint("ak_teams_id_competition_id", x => new { x.id, x.competition_id });
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
                name: "questionnaire_activity_pauses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: false),
                    ended_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_activity_pauses", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_pauses_questionnaire_activities_acti",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_draw_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: true),
                    count = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_draw_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_rules_questionnaire_activities_activity_",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_participants_questionnaire_activities_activit",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_participants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_question_exposures",
                columns: table => new
                {
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_exposed_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_question_exposures", x => new { x.source_id, x.activity_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_question_exposures_questionnaire_activities_a",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_question_exposures_questionnaire_sources_sour",
                        column: x => x.source_id,
                        principalTable: "questionnaire_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_passkey_transports",
                columns: table => new
                {
                    user_passkey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    transport = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_passkey_transports", x => new { x.user_passkey_id, x.position });
                    table.ForeignKey(
                        name: "fk_user_passkey_transports_user_passkeys_user_passkey_id",
                        column: x => x.user_passkey_id,
                        principalTable: "user_passkeys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_mfa_amr_values",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oidc_mfa_amr_values", x => new { x.group_id, x.position });
                    table.ForeignKey(
                        name: "fk_oidc_mfa_amr_values_oidc_mfa_amr_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "oidc_mfa_amr_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_templates",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_kind = table.Column<short>(type: "smallint", nullable: false),
                    operation_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    egress_policy = table.Column<short>(type: "smallint", nullable: false),
                    has_explicit_limits = table.Column<bool>(type: "boolean", nullable: true),
                    ova_source_url = table.Column<string>(type: "text", nullable: true),
                    sha256 = table.Column<string>(type: "text", nullable: true),
                    limits_cpu_millicores = table.Column<long>(type: "bigint", nullable: true),
                    limits_memory_bytes = table.Column<long>(type: "bigint", nullable: true),
                    limits_pids_limit = table.Column<long>(type: "bigint", nullable: true)
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
                name: "challenge_capabilities",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    has_flag_template = table.Column<bool>(type: "boolean", nullable: true),
                    injection_command = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    injection_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    injection_service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    flag_template_body_template = table.Column<string>(type: "text", nullable: true),
                    flag_template_header = table.Column<string>(type: "text", nullable: true),
                    flag_template_leet_literal_text = table.Column<bool>(type: "boolean", nullable: true),
                    image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    target_service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    ready_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    accepts_fix_input = table.Column<bool>(type: "boolean", nullable: true),
                    protocol = table.Column<short>(type: "smallint", nullable: true),
                    url_template = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    container_port = table.Column<int>(type: "integer", nullable: true),
                    vm_id = table.Column<string>(type: "text", nullable: true),
                    guest_port = table.Column<int>(type: "integer", nullable: true),
                    control_capability_definition_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    fix_capability_definition_target_service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    entrypoint = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    fix_capability_definition_timeout_seconds = table.Column<int>(type: "integer", nullable: true),
                    maximum_upload_bytes = table.Column<long>(type: "bigint", nullable: true),
                    current_revision_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_capabilities", x => new { x.challenge_id, x.kind });
                    table.ForeignKey(
                        name: "fk_challenge_capabilities_challenge_definitions_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenge_definitions",
                        principalColumn: "challenge_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_challenge_capabilities_questionnaire_revisions_current_revi",
                        columns: x => new { x.current_revision_id, x.challenge_id },
                        principalTable: "questionnaire_revisions",
                        principalColumns: new[] { "id", "challenge_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_activity_fixed_questionnaires",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    points_per_question = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_activity_fixed_questionnaires", x => new { x.activity_id, x.position });
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_fixed_questionnaires_challenges_chal",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_fixed_questionnaires_questionnaire_a",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_fixed_questionnaires_questionnaire_r",
                        columns: x => new { x.revision_id, x.challenge_id },
                        principalTable: "questionnaire_revisions",
                        principalColumns: new[] { "id", "challenge_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_questions",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    difficulty = table.Column<short>(type: "smallint", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: false),
                    explanation = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_questions", x => new { x.revision_id, x.id });
                    table.UniqueConstraint("ak_questionnaire_questions_version_id", x => x.version_id);
                    table.UniqueConstraint("ak_questionnaire_questions_version_id_id", x => new { x.version_id, x.id });
                    table.UniqueConstraint("ak_questionnaire_questions_version_id_revision_id_id", x => new { x.version_id, x.revision_id, x.id });
                    table.ForeignKey(
                        name: "fk_questionnaire_questions_questionnaire_question_source_sourc",
                        column: x => x.source_id,
                        principalTable: "questionnaire_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_questions_questionnaire_revision_revision_id",
                        column: x => x.revision_id,
                        principalTable: "questionnaire_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_tie_breaks",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<short>(type: "smallint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_round_tie_breaks", x => new { x.round_id, x.position });
                    table.ForeignKey(
                        name: "fk_awd_round_tie_breaks_awd_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "awd_rounds",
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
                    direction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    custom_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    normalized_custom_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    auto_open_at = table.Column<long>(type: "bigint", nullable: true),
                    scoring_ends_at = table.Column<long>(type: "bigint", nullable: true),
                    submission_deadline_at = table.Column<long>(type: "bigint", nullable: true),
                    opening_state = table.Column<short>(type: "smallint", nullable: false),
                    timing_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_timing_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    write_up_deduction_percent = table.Column<int>(type: "integer", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenges", x => x.id);
                    table.UniqueConstraint("ak_competition_challenges_id_competition_id", x => new { x.id, x.competition_id });
                    table.ForeignKey(
                        name: "fk_competition_challenges_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenges_competition_direction_competition_id",
                        columns: x => new { x.competition_id, x.direction_id },
                        principalTable: "competition_directions",
                        principalColumns: new[] { "competition_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenges_competitions_competition_id_mode",
                        columns: x => new { x.competition_id, x.mode },
                        principalTable: "competitions",
                        principalColumns: new[] { "id", "mode" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "awd_competition_tie_breaks",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    field = table.Column<short>(type: "smallint", nullable: false),
                    direction = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_competition_tie_breaks", x => new { x.competition_id, x.position });
                    table.ForeignKey(
                        name: "fk_awd_competition_tie_breaks_competition_mode_configuration_c",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
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
                name: "live_solo_stage_rules",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lane = table.Column<short>(type: "smallint", nullable: false),
                    stage = table.Column<int>(type: "integer", nullable: false),
                    tournament_stage = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    required_wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_stage_rules", x => new { x.competition_id, x.tournament_stage, x.lane, x.stage });
                    table.ForeignKey(
                        name: "fk_live_solo_stage_rules_competition_mode_configuration_compet",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_tournament_stages",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    advance_count = table.Column<int>(type: "integer", nullable: false),
                    swiss_advance_wins = table.Column<int>(type: "integer", nullable: false),
                    swiss_eliminate_losses = table.Column<int>(type: "integer", nullable: false),
                    swiss_qualification_required_wins = table.Column<int>(type: "integer", nullable: false),
                    swiss_elimination_required_wins = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_tournament_stages", x => new { x.competition_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_tournament_stages_competition_mode_configuration_",
                        column: x => x.competition_id,
                        principalTable: "competition_mode_configurations",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_event_items",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    summary_kind = table.Column<short>(type: "smallint", nullable: false),
                    summary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    summary_cheat_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_consultation_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_appeal_status = table.Column<short>(type: "smallint", nullable: true),
                    summary_requires_staff_action = table.Column<bool>(type: "boolean", nullable: false),
                    summary_action_required_since = table.Column<long>(type: "bigint", nullable: true),
                    summary_created_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_updated_at = table.Column<long>(type: "bigint", nullable: false),
                    summary_detected_at = table.Column<long>(type: "bigint", nullable: true),
                    summary_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_related_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_related_team_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    summary_challenge_title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: true),
                    summary_reason_code = table.Column<short>(type: "smallint", nullable: true),
                    summary_subject = table.Column<short>(type: "smallint", nullable: true),
                    summary_actor_display_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    summary_last_changed_sequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_event_items", x => new { x.event_id, x.position });
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_event_items_competition_staff_web",
                        column: x => x.event_id,
                        principalTable: "competition_staff_webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_staff_webhook_deliveries",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    next_attempt_at = table.Column<long>(type: "bigint", nullable: false),
                    lease_until = table.Column<long>(type: "bigint", nullable: true),
                    attempt_token = table.Column<Guid>(type: "uuid", nullable: false),
                    prepared_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_staff_webhook_deliveries", x => new { x.event_id, x.target_id });
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_deliveries_competition_staff_webh",
                        column: x => x.event_id,
                        principalTable: "competition_staff_webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_staff_webhook_deliveries_competition_staff_webh1",
                        column: x => x.target_id,
                        principalTable: "competition_staff_webhook_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff_webhook_category",
                columns: table => new
                {
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_webhook_category", x => new { x.target_id, x.kind });
                    table.ForeignKey(
                        name: "fk_staff_webhook_category_competition_staff_webhook_targets_ta",
                        column: x => x.target_id,
                        principalTable: "competition_staff_webhook_targets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_direction_presets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_direction_presets", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_direction_presets_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_direction_presets_live_solo_question_groups_quest",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
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
                name: "team_progression_badge_states",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_progression_badge_states", x => new { x.team_id, x.badge_id });
                    table.ForeignKey(
                        name: "fk_team_progression_badge_states_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_team_progression_badge_states_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badge_grants",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    awarded_at = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badge_grants", x => new { x.team_id, x.badge_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_user_badge_grants_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_grants_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_grants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_badge_transitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    badge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_badge_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_competition_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_user_badge_transitions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_draw_rule_filters",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_draw_rule_filters", x => new { x.rule_id, x.kind, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_rule_filters_questionnaire_draw_rules_ru",
                        column: x => x.rule_id,
                        principalTable: "questionnaire_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    opened_at = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<long>(type: "bigint", nullable: true),
                    submission_kind = table.Column<short>(type: "smallint", nullable: true),
                    submitted_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_sessions", x => x.id);
                    table.UniqueConstraint("ak_questionnaire_sessions_participant_id_id", x => new { x.participant_id, x.id });
                    table.ForeignKey(
                        name: "fk_questionnaire_sessions_questionnaire_participants_participa",
                        column: x => x.participant_id,
                        principalTable: "questionnaire_participants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_sessions_users_submitted_by_id",
                        column: x => x.submitted_by_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_services",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    image = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    cpu_cores = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: false),
                    memory_mi_b = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_services", x => new { x.challenge_id, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_services_challenge_runtime_templates_chal",
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
                name: "challenge_check_command",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_check_command", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_check_command_challenge_capabilities_challenge_id",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_check_environment",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_check_environment", x => new { x.challenge_id, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_check_environment_challenge_capabilities_challeng",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_check_requirements",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_check_requirements", x => new { x.challenge_id, x.kind });
                    table.ForeignKey(
                        name: "fk_challenge_check_requirements_challenge_capabilities_challen",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_fix_command",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_fix_command", x => new { x.challenge_id, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_fix_command_challenge_capabilities_challenge_id_c",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_flag_environment_targets",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    variable_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_flag_environment_targets", x => new { x.challenge_id, x.service_name });
                    table.ForeignKey(
                        name: "fk_challenge_flag_environment_targets_challenge_capability_def",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_flag_stores",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: true),
                    delivery_kind = table.Column<short>(type: "smallint", nullable: false),
                    injection_command = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    injection_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    preserves_historical_flags = table.Column<bool>(type: "boolean", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_flag_stores", x => new { x.challenge_id, x.id });
                    table.ForeignKey(
                        name: "fk_challenge_flag_stores_challenge_capability_definition_chall",
                        columns: x => new { x.challenge_id, x.capability_kind },
                        principalTable: "challenge_capabilities",
                        principalColumns: new[] { "challenge_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_classifications",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_classifications", x => new { x.revision_id, x.question_id, x.kind, x.normalized_name });
                    table.ForeignKey(
                        name: "fk_questionnaire_classifications_questionnaire_questions_revis",
                        columns: x => new { x.revision_id, x.question_id },
                        principalTable: "questionnaire_questions",
                        principalColumns: new[] { "revision_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_draw_candidates",
                columns: table => new
                {
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_draw_candidates", x => new { x.rule_id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_candidates_questionnaire_draw_rules_rule",
                        column: x => x.rule_id,
                        principalTable: "questionnaire_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_candidates_questionnaire_questions_revis",
                        column: x => x.revision_id,
                        principalTable: "questionnaire_questions",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_candidates_questionnaire_sources_source_",
                        column: x => x.source_id,
                        principalTable: "questionnaire_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_files",
                columns: table => new
                {
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_files", x => new { x.revision_id, x.question_id, x.file_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_files_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_files_questionnaire_questions_revision_id_que",
                        columns: x => new { x.revision_id, x.question_id },
                        principalTable: "questionnaire_questions",
                        principalColumns: new[] { "revision_id", "id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_grading_overrides",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    target_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    mode = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_grading_overrides", x => new { x.activity_id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_grading_overrides_questionnaire_activities_ac",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_grading_overrides_questionnaire_questions_rev",
                        column: x => x.revision_id,
                        principalTable: "questionnaire_questions",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_options",
                columns: table => new
                {
                    question_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    truth_value = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_options", x => new { x.question_version_id, x.id });
                    table.ForeignKey(
                        name: "fk_questionnaire_options_questionnaire_questions_question_vers",
                        columns: x => new { x.question_version_id, x.revision_id, x.question_id },
                        principalTable: "questionnaire_questions",
                        principalColumns: new[] { "version_id", "revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
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
                    generation_epoch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    generation_epoch_round = table.Column<int>(type: "integer", nullable: false),
                    generation_epoch_active_ticks = table.Column<long>(type: "bigint", nullable: false),
                    attack_reward_mode = table.Column<short>(type: "smallint", nullable: false),
                    attack_points = table.Column<long>(type: "bigint", nullable: false),
                    victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: false),
                    service_healthy_points = table.Column<long>(type: "bigint", nullable: false),
                    checker_interval_seconds = table.Column<int>(type: "integer", nullable: false),
                    eligible_team_count = table.Column<int>(type: "integer", nullable: false),
                    flag_header = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    flag_body_template = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
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
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    protected_read_context = table.Column<byte[]>(type: "bytea", maxLength: 32768, nullable: true),
                    public_locator = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
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
                    mode = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    attack_reward_mode = table.Column<short>(type: "smallint", nullable: true),
                    attack_points = table.Column<long>(type: "bigint", nullable: true),
                    victim_defense_pool_points = table.Column<long>(type: "bigint", nullable: true),
                    checker_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    service_healthy_points = table.Column<long>(type: "bigint", nullable: true),
                    service_unhealthy_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_competition_challenge_rules_has_flag_template = table.Column<bool>(type: "boolean", nullable: true),
                    allow_fix_submissions = table.Column<bool>(type: "boolean", nullable: true),
                    awd_overrides_attack_settlement = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_callback_grace_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_confidentiality_points = table.Column<long>(type: "bigint", nullable: true),
                    awd_overrides_consecutive_failure_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_defense_mode = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_final_check_budget_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_flag_lifetime = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_flag_lifetime_unit = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_flags_per_store = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_generation_interval = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_generation_policy = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_historical_lifetime = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_historical_lifetime_unit = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_link_historical_window = table.Column<bool>(type: "boolean", nullable: true),
                    awd_overrides_maximum_rank_multiplier = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_maximum_round_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_overrides_minimum_attack_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_mumble_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_overrides_mumble_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_mumble_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_offline_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_overrides_offline_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_offline_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_ok_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_penalty_mode = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_preset = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_recovering_penalty = table.Column<long>(type: "bigint", nullable: true),
                    awd_overrides_recovering_penalty_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_recovering_ratio = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_retry_count = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_retry_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    awd_overrides_scarcity_alpha = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    awd_overrides_sla_basis = table.Column<short>(type: "smallint", nullable: true),
                    awd_overrides_sla_settlement = table.Column<short>(type: "smallint", nullable: true),
                    awd_flag_body_template = table.Column<string>(type: "text", nullable: true),
                    awd_flag_header = table.Column<string>(type: "text", nullable: true),
                    awd_flag_leet_literal_text = table.Column<bool>(type: "boolean", nullable: true),
                    awd_runtime_allocation = table.Column<short>(type: "smallint", nullable: true),
                    awd_runtime_flag_source = table.Column<short>(type: "smallint", nullable: true),
                    awd_runtime_ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    has_break_score_curve = table.Column<bool>(type: "boolean", nullable: true),
                    has_fix_score_curve = table.Column<bool>(type: "boolean", nullable: true),
                    max_break_submissions = table.Column<int>(type: "integer", nullable: true),
                    max_fix_submissions = table.Column<int>(type: "integer", nullable: true),
                    require_break_before_fix = table.Column<bool>(type: "boolean", nullable: true),
                    flag_wrong_penalty = table.Column<long>(type: "bigint", nullable: true),
                    exploit_succeeded_penalty = table.Column<long>(type: "bigint", nullable: true),
                    service_abnormal_penalty = table.Column<long>(type: "bigint", nullable: true),
                    evaluation_dispatch_mode = table.Column<short>(type: "smallint", nullable: true),
                    has_flag_template = table.Column<bool>(type: "boolean", nullable: true),
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
                    awdp_flag_body_template = table.Column<string>(type: "text", nullable: true),
                    awdp_flag_header = table.Column<string>(type: "text", nullable: true),
                    awdp_flag_leet_literal_text = table.Column<bool>(type: "boolean", nullable: true),
                    awdp_runtime_allocation = table.Column<short>(type: "smallint", nullable: true),
                    awdp_runtime_flag_source = table.Column<short>(type: "smallint", nullable: true),
                    awdp_runtime_ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    has_score_curve = table.Column<bool>(type: "boolean", nullable: true),
                    max_flag_attempts = table.Column<int>(type: "integer", nullable: true),
                    wrong_submission_penalty = table.Column<long>(type: "bigint", nullable: true),
                    ctf_competition_challenge_rules_has_flag_template = table.Column<bool>(type: "boolean", nullable: true),
                    score_settlement_mode = table.Column<short>(type: "smallint", nullable: true),
                    ctf_flag_body_template = table.Column<string>(type: "text", nullable: true),
                    ctf_flag_header = table.Column<string>(type: "text", nullable: true),
                    ctf_flag_leet_literal_text = table.Column<bool>(type: "boolean", nullable: true),
                    ctf_runtime_allocation = table.Column<short>(type: "smallint", nullable: true),
                    ctf_runtime_flag_source = table.Column<short>(type: "smallint", nullable: true),
                    ctf_runtime_ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    score_curve_custom_expression = table.Column<string>(type: "text", nullable: true),
                    score_curve_decay_mode = table.Column<short>(type: "smallint", nullable: true),
                    score_curve_decay_team_count = table.Column<int>(type: "integer", nullable: true),
                    score_curve_initial_points = table.Column<long>(type: "bigint", nullable: true),
                    score_curve_minimum_points = table.Column<long>(type: "bigint", nullable: true),
                    poll_interval_seconds = table.Column<int>(type: "integer", nullable: true),
                    control_points_per_interval = table.Column<long>(type: "bigint", nullable: true),
                    koh_runtime_allocation = table.Column<short>(type: "smallint", nullable: true),
                    koh_runtime_flag_source = table.Column<short>(type: "smallint", nullable: true),
                    koh_runtime_ttl_seconds = table.Column<int>(type: "integer", nullable: true),
                    completion_kind = table.Column<short>(type: "smallint", nullable: true),
                    live_solo_competition_challenge_rules_has_flag_template = table.Column<bool>(type: "boolean", nullable: true),
                    live_solo_flag_body_template = table.Column<string>(type: "text", nullable: true),
                    live_solo_flag_header = table.Column<string>(type: "text", nullable: true),
                    live_solo_flag_leet_literal_text = table.Column<bool>(type: "boolean", nullable: true),
                    live_solo_runtime_allocation = table.Column<short>(type: "smallint", nullable: true),
                    live_solo_runtime_flag_source = table.Column<short>(type: "smallint", nullable: true),
                    live_solo_runtime_ttl_seconds = table.Column<int>(type: "integer", nullable: true)
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
                name: "competition_challenge_tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_tags", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_tags_competition_challenges_competiti",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_writeups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    author_scope_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    next_version_number = table.Column<int>(type: "integer", nullable: false),
                    draft_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    published_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeups", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_competition_challenges_compe",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeups_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_progression_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requires_prerequisites = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    competition_badge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progression_nodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_badges_competitio",
                        column: x => x.competition_badge_id,
                        principalTable: "competition_badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_challenges_compet",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_nodes_competition_progressions_comp",
                        column: x => x.competition_id,
                        principalTable: "competition_progressions",
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
                name: "live_solo_question_materials",
                columns: table => new
                {
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_template_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_question_materials", x => x.competition_challenge_id);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_challenges_challenge_id",
                        column: x => x.challenge_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_challenges_source_template_id",
                        column: x => x.source_template_id,
                        principalTable: "challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_competition_challenges_competi",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_question_materials_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "active_team_memberships",
                columns: table => new
                {
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_active_team_memberships", x => new { x.competition_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_active_team_memberships_team_members_team_id_user_id",
                        columns: x => new { x.team_id, x.user_id },
                        principalTable: "team_members",
                        principalColumns: new[] { "team_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
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
                name: "questionnaire_active_sessions",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_active_sessions", x => x.participant_id);
                    table.ForeignKey(
                        name: "fk_questionnaire_active_sessions_questionnaire_sessions_partic",
                        columns: x => new { x.participant_id, x.session_id },
                        principalTable: "questionnaire_sessions",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_activity_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_activity_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_operations_questionnaire_activities_",
                        column: x => x.activity_id,
                        principalTable: "questionnaire_activities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_operations_questionnaire_questions_r",
                        column: x => x.revision_id,
                        principalTable: "questionnaire_questions",
                        principalColumn: "version_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_operations_questionnaire_sessions_se",
                        column: x => x.session_id,
                        principalTable: "questionnaire_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_activity_operations_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_assignments", x => x.id);
                    table.UniqueConstraint("ak_questionnaire_assignments_id_session_id_revision_id", x => new { x.id, x.session_id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_assignments_questionnaire_revision_revision_i",
                        columns: x => new { x.revision_id, x.challenge_id },
                        principalTable: "questionnaire_revisions",
                        principalColumns: new[] { "id", "challenge_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_assignments_questionnaire_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "questionnaire_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_draw_receipts",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    issued_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_draw_receipts", x => new { x.participant_id, x.source_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_receipts_questionnaire_sessions_particip",
                        columns: x => new { x.participant_id, x.session_id },
                        principalTable: "questionnaire_sessions",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_draw_receipts_questionnaire_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "questionnaire_sources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_issue_receipts",
                columns: table => new
                {
                    participant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_issue_receipts", x => new { x.participant_id, x.request_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_issue_receipts_questionnaire_sessions_partici",
                        columns: x => new { x.participant_id, x.session_id },
                        principalTable: "questionnaire_sessions",
                        principalColumns: new[] { "participant_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_commands",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    is_argument = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_commands", x => new { x.challenge_id, x.service_name, x.is_argument, x.position });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_commands_challenge_runtime_servic",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_environment",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_environment", x => new { x.challenge_id, x.service_name, x.name });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_environment_challenge_runtime_ser",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_runtime_service_internal_ports",
                columns: table => new
                {
                    challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    port = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_challenge_runtime_service_internal_ports", x => new { x.challenge_id, x.service_name, x.port });
                    table.ForeignKey(
                        name: "fk_challenge_runtime_service_internal_ports_challenge_runtime_",
                        columns: x => new { x.challenge_id, x.service_name },
                        principalTable: "challenge_runtime_services",
                        principalColumns: new[] { "challenge_id", "name" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_grading_options",
                columns: table => new
                {
                    activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_grading_options", x => new { x.activity_id, x.revision_id, x.option_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_grading_options_questionnaire_grading_overrid",
                        columns: x => new { x.activity_id, x.revision_id },
                        principalTable: "questionnaire_grading_overrides",
                        principalColumns: new[] { "activity_id", "revision_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_questionnaire_grading_options_questionnaire_options_revisio",
                        columns: x => new { x.revision_id, x.option_id },
                        principalTable: "questionnaire_options",
                        principalColumns: new[] { "question_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "awd_round_store_budgets",
                columns: table => new
                {
                    rules_id = table.Column<Guid>(type: "uuid", nullable: false),
                    store_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    weight = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    flags_per_issue = table.Column<int>(type: "integer", nullable: false),
                    delivery_mode = table.Column<short>(type: "smallint", nullable: false),
                    target_service_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    injection_command = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: true),
                    injection_timeout_seconds = table.Column<int>(type: "integer", nullable: false),
                    preserves_historical_flags = table.Column<bool>(type: "boolean", nullable: false)
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
                    competitive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
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
                name: "competition_challenge_writeup_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    write_up_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    format = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    markdown = table.Column<string>(type: "character varying(262144)", maxLength: 262144, nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<long>(type: "bigint", nullable: false),
                    submitted_at = table.Column<long>(type: "bigint", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<long>(type: "bigint", nullable: true),
                    review_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeup_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_competition_challeng",
                        column: x => x.write_up_id,
                        principalTable: "competition_challenge_writeups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_versions_users_reviewed_by_us",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_progression_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    condition = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_progression_edges", x => x.id);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_competition_progressions_comp",
                        column: x => x.competition_id,
                        principalTable: "competition_progressions",
                        principalColumn: "competition_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_progression_nodes_source_node",
                        column: x => x.source_node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_progression_edges_progression_nodes_target_node",
                        column: x => x.target_node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "team_progression_node_states",
                columns: table => new
                {
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    graph_revision = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    complete = table.Column<bool>(type: "boolean", nullable: false),
                    evaluated_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_team_progression_node_states", x => new { x.team_id, x.node_id });
                    table.ForeignKey(
                        name: "fk_team_progression_node_states_competition_progression_nodes_",
                        column: x => x.node_id,
                        principalTable: "competition_progression_nodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_team_progression_node_states_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "gameplay_facts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: true),
                    questionnaire_assignment_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                    time_eligibility = table.Column<short>(type: "smallint", nullable: false),
                    applied_timing_revision = table.Column<Guid>(type: "uuid", nullable: false),
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
                        name: "fk_gameplay_facts_questionnaire_assignment_questionnaire_assig",
                        column: x => x.questionnaire_assignment_id,
                        principalTable: "questionnaire_assignments",
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
                name: "questionnaire_session_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    applied_grading_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    answered_by_id = table.Column<Guid>(type: "uuid", nullable: true),
                    answered_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_session_questions", x => x.id);
                    table.UniqueConstraint("ak_questionnaire_session_questions_id_revision_id", x => new { x.id, x.revision_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_session_questions_questionnaire_assignments_a",
                        columns: x => new { x.assignment_id, x.session_id, x.questionnaire_revision_id },
                        principalTable: "questionnaire_assignments",
                        principalColumns: new[] { "id", "session_id", "revision_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_session_questions_questionnaire_draw_rules_ru",
                        column: x => x.rule_id,
                        principalTable: "questionnaire_draw_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_session_questions_questionnaire_questions_rev",
                        columns: x => new { x.revision_id, x.questionnaire_revision_id, x.questionnaire_question_id },
                        principalTable: "questionnaire_questions",
                        principalColumns: new[] { "version_id", "revision_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_session_questions_questionnaire_sessions_sess",
                        column: x => x.session_id,
                        principalTable: "questionnaire_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_session_questions_users_answered_by_id",
                        column: x => x.answered_by_id,
                        principalTable: "users",
                        principalColumn: "id",
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
                    received_round_number = table.Column<int>(type: "integer", nullable: true),
                    received_active_ticks = table.Column<long>(type: "bigint", nullable: true),
                    receipt_scorable = table.Column<bool>(type: "boolean", nullable: true),
                    attack_outcome = table.Column<short>(type: "smallint", nullable: true),
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
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempt_started_at = table.Column<long>(type: "bigint", nullable: true),
                    attempt_deadline = table.Column<long>(type: "bigint", nullable: true),
                    received_at = table.Column<long>(type: "bigint", nullable: true),
                    sample_active_ticks = table.Column<long>(type: "bigint", nullable: true),
                    sample_deadline_active_ticks = table.Column<long>(type: "bigint", nullable: true),
                    manifest_complete = table.Column<bool>(type: "boolean", nullable: false),
                    evidence_complete = table.Column<bool>(type: "boolean", nullable: false),
                    adjudicated_at = table.Column<long>(type: "bigint", nullable: true),
                    blocking_fix_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    maintenance_wait_started_at = table.Column<long>(type: "bigint", nullable: true),
                    maintenance_wait_ended_at = table.Column<long>(type: "bigint", nullable: true),
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
                        name: "fk_awd_fact_details_gameplay_facts_blocking_fix_fact_id",
                        column: x => x.blocking_fix_fact_id,
                        principalTable: "gameplay_facts",
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
                    delivery_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    blocking_fix_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    maintenance_wait_started_at = table.Column<long>(type: "bigint", nullable: true),
                    maintenance_wait_ended_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_awd_flag_issues", x => x.flag_id);
                    table.UniqueConstraint("ak_awd_flag_issues_flag_id_competition_id", x => new { x.flag_id, x.competition_id });
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_awd_flag_keys_competition_id_key_version",
                        columns: x => new { x.competition_id, x.key_version },
                        principalTable: "awd_flag_keys",
                        principalColumns: new[] { "competition_id", "version" },
                        onDelete: ReferentialAction.Restrict);
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
                        name: "fk_awd_flag_issues_gameplay_facts_blocking_fix_fact_id",
                        column: x => x.blocking_fix_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_awd_flag_issues_teams_team_id_competition_id",
                        columns: x => new { x.team_id, x.competition_id },
                        principalTable: "teams",
                        principalColumns: new[] { "id", "competition_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_writeup_unlocks",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    deduction_percent = table.Column<int>(type: "integer", nullable: false),
                    unlocked_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_competition_challenge_writeup_unlocks", x => x.gameplay_fact_id);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competition_challenge",
                        column: x => x.competition_challenge_id,
                        principalTable: "competition_challenges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competition_challenge1",
                        column: x => x.version_id,
                        principalTable: "competition_challenge_writeup_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_competitions_competit",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_gameplay_facts_gamepl",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_competition_challenge_writeup_unlocks_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flag_acquisition_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<short>(type: "smallint", nullable: false),
                    required = table.Column<short>(type: "smallint", nullable: false),
                    acquired = table.Column<short>(type: "smallint", nullable: false),
                    source = table.Column<short>(type: "smallint", nullable: false),
                    captured_at = table.Column<long>(type: "bigint", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    runtime_started_at = table.Column<long>(type: "bigint", nullable: true),
                    attachment_download_fact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attachment_downloaded_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flag_acquisition_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_flag_acquisition_evidence_gameplay_facts_id",
                        column: x => x.id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
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
                    execution_scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purpose = table.Column<short>(type: "smallint", nullable: false),
                    access_mode = table.Column<short>(type: "smallint", nullable: false),
                    traffic_capture_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    traffic_capture_limit_bytes = table.Column<long>(type: "bigint", nullable: true),
                    traffic_capture_reserved_bytes = table.Column<long>(type: "bigint", nullable: false),
                    test_flag_delivery = table.Column<short>(type: "smallint", nullable: true),
                    test_flag_state = table.Column<short>(type: "smallint", nullable: true),
                    template_test_use = table.Column<short>(type: "smallint", nullable: true),
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
                name: "questionnaire_answer_receipts",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resulting_revision = table.Column<Guid>(type: "uuid", nullable: false),
                    saved_at = table.Column<long>(type: "bigint", nullable: false),
                    selection_digest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_answer_receipts", x => new { x.item_id, x.request_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_answer_receipts_questionnaire_session_questio",
                        column: x => x.item_id,
                        principalTable: "questionnaire_session_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_answer_receipts_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_session_options",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    selected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_session_options", x => new { x.item_id, x.option_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_session_options_questionnaire_options_questio",
                        columns: x => new { x.question_version_id, x.option_id },
                        principalTable: "questionnaire_options",
                        principalColumns: new[] { "question_version_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_questionnaire_session_options_questionnaire_session_questio",
                        columns: x => new { x.item_id, x.question_version_id },
                        principalTable: "questionnaire_session_questions",
                        principalColumns: new[] { "id", "revision_id" },
                        onDelete: ReferentialAction.Restrict);
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
                name: "capability_outcomes",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observed_at = table.Column<long>(type: "bigint", nullable: false),
                    execution_state = table.Column<short>(type: "smallint", nullable: false),
                    execution_failure = table.Column<short>(type: "smallint", nullable: true),
                    match = table.Column<short>(type: "smallint", nullable: true),
                    result = table.Column<short>(type: "smallint", nullable: true),
                    controller_identifier = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    fix_outcome_result = table.Column<short>(type: "smallint", nullable: true),
                    exit_code = table.Column<int>(type: "integer", nullable: true),
                    execution_uncertain = table.Column<bool>(type: "boolean", nullable: true),
                    execution_identity = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    revision_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_capability_outcomes", x => new { x.gameplay_fact_id, x.kind });
                    table.ForeignKey(
                        name: "fk_capability_outcomes_gameplay_facts_gameplay_fact_id",
                        column: x => x.gameplay_fact_id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_capability_outcomes_questionnaire_revisions_revision_id",
                        column: x => x.revision_id,
                        principalTable: "questionnaire_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_capability_outcomes_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fix_execution_details",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_kind = table.Column<short>(type: "smallint", nullable: false),
                    phase = table.Column<short>(type: "smallint", nullable: false),
                    failed_phase = table.Column<short>(type: "smallint", nullable: true),
                    mutation_blocked = table.Column<bool>(type: "boolean", nullable: false),
                    deadline = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    mutation_started_at = table.Column<long>(type: "bigint", nullable: true),
                    mutation_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    last_confirmed_running_at = table.Column<long>(type: "bigint", nullable: true),
                    applied_at = table.Column<long>(type: "bigint", nullable: true),
                    finalized_at = table.Column<long>(type: "bigint", nullable: true),
                    work_completed_at = table.Column<long>(type: "bigint", nullable: true),
                    judged_at = table.Column<long>(type: "bigint", nullable: true),
                    execution_failure = table.Column<short>(type: "smallint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fix_execution_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_fix_execution_details_gameplay_facts_id",
                        column: x => x.id,
                        principalTable: "gameplay_facts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fix_execution_details_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
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
                    budget_cpu_millicores = table.Column<long>(type: "bigint", nullable: false),
                    budget_pids_limit = table.Column<long>(type: "bigint", nullable: false),
                    limit_memory_bytes = table.Column<long>(type: "bigint", nullable: false),
                    limit_cpu_millicores = table.Column<long>(type: "bigint", nullable: false),
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
                    public_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    owned_network_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    discovery_service_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    container_runtime_receipt_created_at = table.Column<long>(type: "bigint", nullable: true),
                    execution_scope_id = table.Column<Guid>(type: "uuid", nullable: true),
                    isolation_state = table.Column<short>(type: "smallint", nullable: true),
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
                name: "template_capability_tests",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    accepted_at = table.Column<long>(type: "bigint", nullable: false),
                    deadline = table.Column<long>(type: "bigint", nullable: false),
                    mutation_started_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure = table.Column<short>(type: "smallint", nullable: true),
                    failure_stage = table.Column<short>(type: "smallint", nullable: true),
                    fix_result = table.Column<short>(type: "smallint", nullable: true),
                    fix_exit_code = table.Column<int>(type: "integer", nullable: true),
                    fix_execution_uncertain = table.Column<bool>(type: "boolean", nullable: false),
                    check_completed = table.Column<bool>(type: "boolean", nullable: false),
                    cancellation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    check_observed_at = table.Column<long>(type: "bigint", nullable: true),
                    check_started_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_capability_tests", x => x.runtime_instance_id);
                    table.ForeignKey(
                        name: "fk_template_capability_tests_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_capability_tests_runtime_instances_runtime_instanc",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_template_capability_tests_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "break_flag_matches",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_break_flag_matches", x => new { x.gameplay_fact_id, x.flag_id });
                    table.ForeignKey(
                        name: "fk_break_flag_matches_capability_outcome_gameplay_fact_id_capa",
                        columns: x => new { x.gameplay_fact_id, x.capability_kind },
                        principalTable: "capability_outcomes",
                        principalColumns: new[] { "gameplay_fact_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_break_flag_matches_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "check_flag_contexts",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_read_context = table.Column<byte[]>(type: "bytea", maxLength: 32768, nullable: true),
                    public_locator = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_flag_contexts", x => new { x.gameplay_fact_id, x.flag_id });
                    table.ForeignKey(
                        name: "fk_check_flag_contexts_capability_outcomes_gameplay_fact_id_ca",
                        columns: x => new { x.gameplay_fact_id, x.capability_kind },
                        principalTable: "capability_outcomes",
                        principalColumns: new[] { "gameplay_fact_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_check_flag_contexts_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "check_probes",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<short>(type: "smallint", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_check_probes", x => new { x.gameplay_fact_id, x.position });
                    table.ForeignKey(
                        name: "fk_check_probes_capability_outcomes_gameplay_fact_id_capabilit",
                        columns: x => new { x.gameplay_fact_id, x.capability_kind },
                        principalTable: "capability_outcomes",
                        principalColumns: new[] { "gameplay_fact_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_check_probes_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_question_results",
                columns: table => new
                {
                    gameplay_fact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    result = table.Column<short>(type: "smallint", nullable: false),
                    capability_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_question_results", x => new { x.gameplay_fact_id, x.question_id });
                    table.ForeignKey(
                        name: "fk_questionnaire_question_results_capability_outcomes_gameplay",
                        columns: x => new { x.gameplay_fact_id, x.capability_kind },
                        principalTable: "capability_outcomes",
                        principalColumns: new[] { "gameplay_fact_id", "kind" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    internal_host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_services", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_services_runtime_receipts_runtime_instance_",
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

            migrationBuilder.CreateTable(
                name: "template_check_flag_contexts",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_read_context = table.Column<byte[]>(type: "bytea", maxLength: 32768, nullable: true),
                    public_locator = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_check_flag_contexts", x => new { x.runtime_instance_id, x.flag_id });
                    table.ForeignKey(
                        name: "fk_template_check_flag_contexts_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_check_flag_contexts_template_capability_tests_runt",
                        column: x => x.runtime_instance_id,
                        principalTable: "template_capability_tests",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_check_probes",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_check_probes", x => new { x.runtime_instance_id, x.position });
                    table.ForeignKey(
                        name: "fk_template_check_probes_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_check_probes_template_capability_tests_runtime_ins",
                        column: x => x.runtime_instance_id,
                        principalTable: "template_capability_tests",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "template_check_requirements",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    flag_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_template_check_requirements", x => new { x.runtime_instance_id, x.position });
                    table.ForeignKey(
                        name: "fk_template_check_requirements_challenge_flags_flag_id",
                        column: x => x.flag_id,
                        principalTable: "challenge_flags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_template_check_requirements_template_capability_tests_runti",
                        column: x => x.runtime_instance_id,
                        principalTable: "template_capability_tests",
                        principalColumn: "runtime_instance_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "runtime_receipt_service_ports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    container_port = table.Column<int>(type: "integer", nullable: false),
                    host_port = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runtime_receipt_service_ports", x => x.id);
                    table.ForeignKey(
                        name: "fk_runtime_receipt_service_ports_runtime_receipt_services_serv",
                        column: x => x.service_id,
                        principalTable: "runtime_receipt_services",
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
                name: "live_solo_adjudications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    forfeiting_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_match_state = table.Column<short>(type: "smallint", nullable: false),
                    match_state = table.Column<short>(type: "smallint", nullable: false),
                    previous_round_state = table.Column<short>(type: "smallint", nullable: true),
                    round_state = table.Column<short>(type: "smallint", nullable: true),
                    previous_left_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_timeline_revision = table.Column<long>(type: "bigint", nullable: true),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_adjudications", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_teams_forfeiting_team_id",
                        column: x => x.forfeiting_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_adjudications_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_attachment_assignments",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_attachment_assignments", x => new { x.round_question_id, x.team_id });
                    table.ForeignKey(
                        name: "fk_live_solo_attachment_assignments_challenge_attachment_attac",
                        column: x => x.attachment_id,
                        principalTable: "challenge_attachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_attachment_assignments_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_correction_matches",
                columns: table => new
                {
                    correction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_state = table.Column<short>(type: "smallint", nullable: false),
                    was_started = table.Column<bool>(type: "boolean", nullable: false),
                    replacement_match_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_correction_matches", x => new { x.correction_id, x.match_id });
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
                name: "live_solo_fix_targets",
                columns: table => new
                {
                    runtime_instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    side = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_fix_targets", x => x.runtime_instance_id);
                    table.ForeignKey(
                        name: "fk_live_solo_fix_targets_runtime_instances_runtime_instance_id",
                        column: x => x.runtime_instance_id,
                        principalTable: "runtime_instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_match_directions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: true),
                    drawn_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_match_directions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_match_directions_live_solo_question_groups_questi",
                        column: x => x.question_group_id,
                        principalTable: "live_solo_question_groups",
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
                    tournament_stage = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    swiss_wins = table.Column<int>(type: "integer", nullable: true),
                    swiss_losses = table.Column<int>(type: "integer", nullable: true),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    conditional = table.Column<bool>(type: "boolean", nullable: false),
                    teammates_may_view_questions = table.Column<bool>(type: "boolean", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    end_reason = table.Column<short>(type: "smallint", nullable: false),
                    result_left_wins = table.Column<int>(type: "integer", nullable: true),
                    result_right_wins = table.Column<int>(type: "integer", nullable: true),
                    result_updated_at = table.Column<long>(type: "bigint", nullable: true),
                    current_round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    current_media_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    pending_correction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    bracket_generation = table.Column<int>(type: "integer", nullable: false),
                    superseded_at = table.Column<long>(type: "bigint", nullable: true),
                    replacement_match_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    completed_at = table.Column<long>(type: "bigint", nullable: true),
                    deleted_at = table.Column<long>(type: "bigint", nullable: true),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                        name: "fk_live_solo_matches_live_solo_matches_replacement_match_id",
                        column: x => x.replacement_match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_matches_teams_winner_team_id",
                        column: x => x.winner_team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_matches_users_deleted_by_user_id",
                        column: x => x.deleted_by_user_id,
                        principalTable: "users",
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
                name: "live_solo_result_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    previous_winner_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_left_wins = table.Column<int>(type: "integer", nullable: false),
                    previous_right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true),
                    resolution_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_result_corrections", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_result_corrections_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "users",
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
                    static_flag_evidence_required = table.Column<bool>(type: "boolean", nullable: false),
                    platform_streaming_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    replay = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    question_group_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    selection_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    selection_kind = table.Column<short>(type: "smallint", nullable: true),
                    selected_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    question_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction_draw_id = table.Column<Guid>(type: "uuid", nullable: true),
                    limit_seconds = table.Column<int>(type: "integer", nullable: false),
                    countdown_seconds = table.Column<int>(type: "integer", nullable: false),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: false),
                    result_revision = table.Column<long>(type: "bigint", nullable: false),
                    end_reason = table.Column<short>(type: "smallint", nullable: false),
                    result_left_wins = table.Column<int>(type: "integer", nullable: true),
                    result_right_wins = table.Column<int>(type: "integer", nullable: true),
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
                        name: "fk_live_solo_rounds_live_solo_match_directions_direction_draw_",
                        column: x => x.direction_draw_id,
                        principalTable: "live_solo_match_directions",
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
                    table.ForeignKey(
                        name: "fk_live_solo_rounds_users_selected_by_user_id",
                        column: x => x.selected_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_viewer_leases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_viewer_leases", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_competitions_competition_id",
                        column: x => x.competition_id,
                        principalTable: "competitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_viewer_leases_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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
                name: "live_solo_rescue_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    requested_at = table.Column<long>(type: "bigint", nullable: false),
                    judge_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    decision_reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    resolved_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_rescue_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_live_solo_matches_match_id",
                        column: x => x.match_id,
                        principalTable: "live_solo_matches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_judge_user_id",
                        column: x => x.judge_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_rescue_requests_users_user_id",
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
                    preparation_failure = table.Column<short>(type: "smallint", nullable: false),
                    environment_required = table.Column<bool>(type: "boolean", nullable: true),
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
                name: "live_solo_round_roster_members",
                columns: table => new
                {
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_round_roster_members", x => new { x.round_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_live_solo_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "live_solo_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_round_roster_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                name: "live_solo_opened_contents",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    random_attachment = table.Column<bool>(type: "boolean", nullable: false),
                    completion_kind = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_contents", x => x.round_question_id);
                    table.ForeignKey(
                        name: "fk_live_solo_opened_contents_live_solo_round_questions_round_q",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateTable(
                name: "live_solo_opened_attachments",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_attachments", x => new { x.round_question_id, x.attachment_id });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_attachments_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_opened_attachments_live_solo_opened_content_round",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_opened_hints",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    hint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    published_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_hints", x => new { x.round_question_id, x.hint_id });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_hints_live_solo_opened_contents_round_ques",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_opened_tags",
                columns: table => new
                {
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_opened_tags", x => new { x.round_question_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_opened_tags_live_solo_opened_contents_round_quest",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_opened_contents",
                        principalColumn: "round_question_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_media_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<short>(type: "smallint", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    identity = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    token_version = table.Column<int>(type: "integer", nullable: false),
                    authentication_method = table.Column<short>(type: "smallint", nullable: false),
                    authenticated_at = table.Column<long>(type: "bigint", nullable: false),
                    mfa_source = table.Column<short>(type: "smallint", nullable: false),
                    mfa_authenticated_at = table.Column<long>(type: "bigint", nullable: true),
                    mfa_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    trust_policy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    primary_credential_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<long>(type: "bigint", nullable: false),
                    revoked_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_media_grants", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_media_grants_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
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
                    recording_retention_days = table.Column<int>(type: "integer", nullable: false),
                    current_program_capture_id = table.Column<Guid>(type: "uuid", nullable: true),
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
                name: "live_solo_program_captures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrency_stamp = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false),
                    egress_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    created_at = table.Column<long>(type: "bigint", nullable: false),
                    requested_at = table.Column<long>(type: "bigint", nullable: true),
                    video_policy_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    video_maximum_width = table.Column<int>(type: "integer", nullable: true),
                    video_maximum_height = table.Column<int>(type: "integer", nullable: true),
                    video_maximum_frames_per_second = table.Column<int>(type: "integer", nullable: true),
                    video_bitrate_bits_per_second = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<long>(type: "bigint", nullable: true),
                    ended_at = table.Column<long>(type: "bigint", nullable: true),
                    imported_at = table.Column<long>(type: "bigint", nullable: true),
                    raw_removed_at = table.Column<long>(type: "bigint", nullable: true),
                    next_segment_sequence = table.Column<long>(type: "bigint", nullable: false),
                    imported_through = table.Column<long>(type: "bigint", nullable: true),
                    rotation_requested = table.Column<bool>(type: "boolean", nullable: false),
                    last_fragment_imported_at = table.Column<long>(type: "bigint", nullable: true),
                    stalled_at = table.Column<long>(type: "bigint", nullable: true),
                    raw_cleanup_authorized_at = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_captures", x => x.id);
                    table.UniqueConstraint("ak_live_solo_program_captures_id_media_session_id", x => new { x.id, x.media_session_id });
                    table.ForeignKey(
                        name: "fk_live_solo_program_captures_live_solo_media_sessions_media_s",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_program_frames",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<short>(type: "smallint", nullable: false),
                    match_revision = table.Column<Guid>(type: "uuid", nullable: true),
                    public_at = table.Column<long>(type: "bigint", nullable: true),
                    winner_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    match_state = table.Column<short>(type: "smallint", nullable: false),
                    required_wins = table.Column<int>(type: "integer", nullable: false),
                    left_wins = table.Column<int>(type: "integer", nullable: false),
                    right_wins = table.Column<int>(type: "integer", nullable: false),
                    left_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    right_team_id = table.Column<Guid>(type: "uuid", nullable: true),
                    left_team_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    right_team_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    round_id = table.Column<Guid>(type: "uuid", nullable: true),
                    round_number = table.Column<int>(type: "integer", nullable: true),
                    round_state = table.Column<short>(type: "smallint", nullable: true),
                    timeline_revision = table.Column<long>(type: "bigint", nullable: true),
                    active_elapsed_milliseconds = table.Column<long>(type: "bigint", nullable: false),
                    limit_seconds = table.Column<int>(type: "integer", nullable: true),
                    paused = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_frames", x => x.id);
                    table.UniqueConstraint("ak_live_solo_program_frames_id_media_session_id", x => new { x.id, x.media_session_id });
                    table.ForeignKey(
                        name: "fk_live_solo_program_frames_live_solo_media_sessions_media_ses",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_recording_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_hold = table.Column<bool>(type: "boolean", nullable: false),
                    hold = table.Column<bool>(type: "boolean", nullable: false),
                    previous_published = table.Column<bool>(type: "boolean", nullable: false),
                    published = table.Column<bool>(type: "boolean", nullable: false),
                    previous_state = table.Column<short>(type: "smallint", nullable: true),
                    state = table.Column<short>(type: "smallint", nullable: true),
                    replacement_recording_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_recording_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_recording_decisions_live_solo_media_sessions_medi",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_recording_decisions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
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
                    failure = table.Column<short>(type: "smallint", nullable: true),
                    video_track_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    chunk = table.Column<int>(type: "integer", nullable: false),
                    reserved_bytes = table.Column<long>(type: "bigint", nullable: false),
                    video_policy_stamp = table.Column<Guid>(type: "uuid", nullable: true),
                    video_maximum_width = table.Column<int>(type: "integer", nullable: true),
                    video_maximum_height = table.Column<int>(type: "integer", nullable: true),
                    video_maximum_frames_per_second = table.Column<int>(type: "integer", nullable: true),
                    video_bitrate_bits_per_second = table.Column<int>(type: "integer", nullable: true),
                    raw_removed_at = table.Column<long>(type: "bigint", nullable: true),
                    requested_at = table.Column<long>(type: "bigint", nullable: true),
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
                name: "live_solo_program_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    program_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    media_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<short>(type: "smallint", nullable: false),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    occurred_at = table.Column<long>(type: "bigint", nullable: false),
                    previous_state = table.Column<short>(type: "smallint", nullable: false),
                    state = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_decisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_live_solo_media_sessions_media_",
                        column: x => x.media_session_id,
                        principalTable: "live_solo_media_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_live_solo_program_captures_prog",
                        column: x => x.program_capture_id,
                        principalTable: "live_solo_program_captures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_decisions_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "live_solo_program_frame_questions",
                columns: table => new
                {
                    frame_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    canonical_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    has_static_answer = table.Column<bool>(type: "boolean", nullable: false),
                    round_question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competition_challenge_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    opened_at = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_live_solo_program_frame_questions", x => new { x.frame_id, x.position });
                    table.ForeignKey(
                        name: "fk_live_solo_program_frame_questions_live_solo_program_frames_",
                        column: x => x.frame_id,
                        principalTable: "live_solo_program_frames",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_live_solo_program_frame_questions_live_solo_round_questions",
                        column: x => x.round_question_id,
                        principalTable: "live_solo_round_questions",
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
                    program_capture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    frame_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.ForeignKey(
                        name: "fk_live_solo_program_segments_live_solo_program_captures_progr",
                        columns: x => new { x.program_capture_id, x.media_session_id },
                        principalTable: "live_solo_program_captures",
                        principalColumns: new[] { "id", "media_session_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_live_solo_program_segments_live_solo_program_frames_frame_i",
                        columns: x => new { x.frame_id, x.media_session_id },
                        principalTable: "live_solo_program_frames",
                        principalColumns: new[] { "id", "media_session_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "platform_settings",
                columns: new[] { "id", "concurrency_stamp", "description", "email_password_reset_cooldown_seconds", "email_password_reset_max_requests_per_hour", "email_password_reset_token_lifetime_minutes", "email_smtp_from_address", "email_smtp_from_name", "email_smtp_host", "email_smtp_password_ciphertext", "email_smtp_port", "email_smtp_security_mode", "email_smtp_timeout_seconds", "email_smtp_user_name", "email_verification_enabled", "email_verification_resend_cooldown_seconds", "email_verification_token_lifetime_minutes", "human_verification_cap_secret_ciphertext", "human_verification_cap_server_url", "human_verification_cap_site_key", "human_verification_enabled", "human_verification_evaluation_enabled", "human_verification_provider", "human_verification_runtime_enabled", "human_verification_turnstile_secret_ciphertext", "human_verification_turnstile_site_key", "live_solo_video_maximum_bitrate_bits_per_second", "live_solo_video_maximum_frames_per_second", "live_solo_video_maximum_height", "live_solo_video_maximum_width", "live_solo_video_policy_stamp", "logo_file_id", "mfa_policy", "mfa_policy_stamp", "name", "public_base_url", "sso_enabled", "updated_at" },
                values: new object[] { (short)1, new Guid("00000000-0000-0000-0000-000000000001"), null, 60, 5, 30, "", "NoCTF", "", null, 587, null, 30, "", false, 60, 1440, null, "", "", true, true, null, true, null, "", 1000000, 10, 720, 1280, new Guid("00000000-0000-0000-0000-000000000005"), null, (short)0, new Guid("00000000-0000-0000-0000-000000000002"), "NoCTF", "http://localhost:5000", false, 621355968000000000L });

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
                name: "ix_active_team_memberships_team_id_user_id",
                table: "active_team_memberships",
                columns: new[] { "team_id", "user_id" },
                unique: true);

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
                name: "ix_awd_competition_tie_breaks_competition_id_field",
                table: "awd_competition_tie_breaks",
                columns: new[] { "competition_id", "field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_fact_details_blocking_fix_fact_id",
                table: "awd_fact_details",
                column: "blocking_fix_fact_id");

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
                name: "ix_awd_flag_issues_blocking_fix_fact_id",
                table: "awd_flag_issues",
                column: "blocking_fix_fact_id");

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_competition_id_competition_challenge_id_tea",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "competition_challenge_id", "team_id", "round_id", "store_id", "generation_slot", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_flag_issues_competition_id_key_version",
                table: "awd_flag_issues",
                columns: new[] { "competition_id", "key_version" });

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
                name: "ix_awd_round_tie_breaks_round_id_field",
                table: "awd_round_tie_breaks",
                columns: new[] { "round_id", "field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_rounds_competition_id_number",
                table: "awd_rounds",
                columns: new[] { "competition_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_awd_rounds_competition_id_starts_at_ends_at",
                table: "awd_rounds",
                columns: new[] { "competition_id", "starts_at", "ends_at" });

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

            migrationBuilder.CreateIndex(
                name: "ix_break_flag_matches_flag_id",
                table: "break_flag_matches",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_break_flag_matches_gameplay_fact_id_capability_kind",
                table: "break_flag_matches",
                columns: new[] { "gameplay_fact_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_capability_outcomes_revision_id",
                table: "capability_outcomes",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_capability_outcomes_runtime_instance_id",
                table: "capability_outcomes",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_challenge_id",
                table: "challenge_attachments",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_attachments_file_id",
                table: "challenge_attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_challenge_capabilities_current_revision_id_challenge_id",
                table: "challenge_capabilities",
                columns: new[] { "current_revision_id", "challenge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_check_command_challenge_id_capability_kind",
                table: "challenge_check_command",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_check_environment_challenge_id_capability_kind",
                table: "challenge_check_environment",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_check_requirements_challenge_id_capability_kind",
                table: "challenge_check_requirements",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_fix_command_challenge_id_capability_kind",
                table: "challenge_fix_command",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flag_environment_targets_challenge_id_capability_",
                table: "challenge_flag_environment_targets",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flag_environment_targets_challenge_id_position",
                table: "challenge_flag_environment_targets",
                columns: new[] { "challenge_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flag_stores_challenge_id_capability_kind",
                table: "challenge_flag_stores",
                columns: new[] { "challenge_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flag_stores_challenge_id_name",
                table: "challenge_flag_stores",
                columns: new[] { "challenge_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_challenge_flag_stores_challenge_id_position",
                table: "challenge_flag_stores",
                columns: new[] { "challenge_id", "position" },
                unique: true);

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
                name: "ix_challenge_runtime_services_challenge_id_position",
                table: "challenge_runtime_services",
                columns: new[] { "challenge_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_challenge_sources_canonical_challenge_id",
                table: "challenge_sources",
                column: "canonical_challenge_id");

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
                name: "ix_check_flag_contexts_flag_id",
                table: "check_flag_contexts",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_check_flag_contexts_gameplay_fact_id_capability_kind",
                table: "check_flag_contexts",
                columns: new[] { "gameplay_fact_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_check_probes_flag_id",
                table: "check_probes",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_check_probes_gameplay_fact_id_capability_kind",
                table: "check_probes",
                columns: new[] { "gameplay_fact_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_command_receipts_user_id_operation_competition_id_resource_",
                table: "command_receipts",
                columns: new[] { "user_id", "operation", "competition_id", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_badges_competition_id",
                table: "competition_badges",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_badges_image_file_id",
                table: "competition_badges",
                column: "image_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_blood_announcements_competition_challenge_id_po",
                table: "competition_blood_announcements",
                columns: new[] { "competition_challenge_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_hints_competition_challenge_id",
                table: "competition_challenge_hints",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_tags_competition_challenge_id_normali",
                table: "competition_challenge_tags",
                columns: new[] { "competition_challenge_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_competition_challenge",
                table: "competition_challenge_writeup_unlocks",
                column: "competition_challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_competition_id",
                table: "competition_challenge_writeup_unlocks",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_team_id_competition_c",
                table: "competition_challenge_writeup_unlocks",
                columns: new[] { "team_id", "competition_challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_unlocks_version_id",
                table: "competition_challenge_writeup_unlocks",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_actor_user_id",
                table: "competition_challenge_writeup_versions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_file_id",
                table: "competition_challenge_writeup_versions",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_reviewed_by_user_id",
                table: "competition_challenge_writeup_versions",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeup_versions_write_up_id_number",
                table: "competition_challenge_writeup_versions",
                columns: new[] { "write_up_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_competition_challenge_id_sou",
                table: "competition_challenge_writeups",
                columns: new[] { "competition_challenge_id", "source", "author_scope_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_competition_id",
                table: "competition_challenge_writeups",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenge_writeups_team_id",
                table: "competition_challenge_writeups",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_challenge_id",
                table: "competition_challenges",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_challenge_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_challenges_competition_id_direction_id",
                table: "competition_challenges",
                columns: new[] { "competition_id", "direction_id" });

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
                name: "ix_competition_directions_competition_id_normalized_name",
                table: "competition_directions",
                columns: new[] { "competition_id", "normalized_name" },
                unique: true);

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
                name: "ix_competition_progression_edges_competition_id_source_node_id",
                table: "competition_progression_edges",
                columns: new[] { "competition_id", "source_node_id", "target_node_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_edges_source_node_id",
                table: "competition_progression_edges",
                column: "source_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_edges_target_node_id",
                table: "competition_progression_edges",
                column: "target_node_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_badge_id",
                table: "competition_progression_nodes",
                column: "competition_badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_challenge_id",
                table: "competition_progression_nodes",
                column: "competition_challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_progression_nodes_competition_id",
                table: "competition_progression_nodes",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_score_presets_competition_id_normalized_name",
                table: "competition_score_presets",
                columns: new[] { "competition_id", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_deliveries_state_next_attempt_at",
                table: "competition_staff_webhook_deliveries",
                columns: new[] { "state", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_deliveries_target_id",
                table: "competition_staff_webhook_deliveries",
                column: "target_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_events_competition_id_sequence",
                table: "competition_staff_webhook_events",
                columns: new[] { "competition_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_targets_approved_by_id",
                table: "competition_staff_webhook_targets",
                column: "approved_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_competition_staff_webhook_targets_competition_id_deleted",
                table: "competition_staff_webhook_targets",
                columns: new[] { "competition_id", "deleted" });

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
                name: "ix_competition_webhook_deliveries_competition_id_state_next_re",
                table: "competition_webhook_deliveries",
                columns: new[] { "competition_id", "state", "next_retry_at" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_outbox_events_competition_id_next_dispa",
                table: "competition_webhook_outbox_events",
                columns: new[] { "competition_id", "next_dispatch_at", "sequence" });

            migrationBuilder.CreateIndex(
                name: "ix_competition_webhook_outbox_events_event_id",
                table: "competition_webhook_outbox_events",
                column: "event_id",
                unique: true);

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
                name: "ix_fix_execution_details_runtime_instance_id",
                table: "fix_execution_details",
                column: "runtime_instance_id");

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
                name: "ix_gameplay_facts_questionnaire_assignment_id",
                table: "gameplay_facts",
                column: "questionnaire_assignment_id",
                unique: true);

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
                name: "ix_live_solo_active_team_slots_match_id",
                table: "live_solo_active_team_slots",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_active_team_slots_team_id",
                table: "live_solo_active_team_slots",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_actor_user_id",
                table: "live_solo_adjudications",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_forfeiting_team_id",
                table: "live_solo_adjudications",
                column: "forfeiting_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_match_id_occurred_at",
                table: "live_solo_adjudications",
                columns: new[] { "match_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_adjudications_round_id",
                table: "live_solo_adjudications",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_attachment_assignments_attachment_id",
                table: "live_solo_attachment_assignments",
                column: "attachment_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_attachment_assignments_team_id",
                table: "live_solo_attachment_assignments",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_correction_matches_match_id",
                table: "live_solo_correction_matches",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_correction_matches_replacement_match_id",
                table: "live_solo_correction_matches",
                column: "replacement_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_direction_presets_competition_id",
                table: "live_solo_direction_presets",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_direction_presets_question_group_id",
                table: "live_solo_direction_presets",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_download_evidence_round_question_id",
                table: "live_solo_download_evidence",
                column: "round_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_fix_targets_round_question_id_side",
                table: "live_solo_fix_targets",
                columns: new[] { "round_question_id", "side" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_position",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_question_group_id",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "question_group_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_match_id_round_number",
                table: "live_solo_match_directions",
                columns: new[] { "match_id", "round_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_directions_question_group_id",
                table: "live_solo_match_directions",
                column: "question_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_slots_source_match_id",
                table: "live_solo_match_slots",
                column: "source_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_match_slots_team_id",
                table: "live_solo_match_slots",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_competition_id_tournament_stage_lane_stag",
                table: "live_solo_matches",
                columns: new[] { "competition_id", "tournament_stage", "lane", "stage", "position", "bracket_generation" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_current_media_session_id",
                table: "live_solo_matches",
                column: "current_media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_current_round_id",
                table: "live_solo_matches",
                column: "current_round_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_deleted_by_user_id",
                table: "live_solo_matches",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_pending_correction_id",
                table: "live_solo_matches",
                column: "pending_correction_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_replacement_match_id",
                table: "live_solo_matches",
                column: "replacement_match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_matches_winner_team_id",
                table: "live_solo_matches",
                column: "winner_team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_grants_media_session_id_user_id_role",
                table: "live_solo_media_grants",
                columns: new[] { "media_session_id", "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_media_grants_user_id",
                table: "live_solo_media_grants",
                column: "user_id");

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
                name: "ix_live_solo_media_sessions_current_program_capture_id_id",
                table: "live_solo_media_sessions",
                columns: new[] { "current_program_capture_id", "id" });

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
                name: "ix_live_solo_opened_attachments_file_id",
                table: "live_solo_opened_attachments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_opened_attachments_round_question_id_position",
                table: "live_solo_opened_attachments",
                columns: new[] { "round_question_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_opened_hints_round_question_id_position",
                table: "live_solo_opened_hints",
                columns: new[] { "round_question_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_pause_intervals_round_id_started_at",
                table: "live_solo_pause_intervals",
                columns: new[] { "round_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_captures_media_session_id",
                table: "live_solo_program_captures",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_captures_state_requested_at",
                table: "live_solo_program_captures",
                columns: new[] { "state", "requested_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_actor_user_id",
                table: "live_solo_program_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_media_session_id",
                table: "live_solo_program_decisions",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_decisions_program_capture_id_occurred_at",
                table: "live_solo_program_decisions",
                columns: new[] { "program_capture_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frame_questions_round_question_id",
                table: "live_solo_program_frame_questions",
                column: "round_question_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_match_revisi",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "kind", "match_revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_frames_media_session_id_kind_occurred_at",
                table: "live_solo_program_frames",
                columns: new[] { "media_session_id", "kind", "occurred_at" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_file_id",
                table: "live_solo_program_segments",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_frame_id_media_session_id",
                table: "live_solo_program_segments",
                columns: new[] { "frame_id", "media_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_media_session_id",
                table: "live_solo_program_segments",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_program_capture_id_media_session",
                table: "live_solo_program_segments",
                columns: new[] { "program_capture_id", "media_session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_program_segments_program_capture_id_sequence",
                table: "live_solo_program_segments",
                columns: new[] { "program_capture_id", "sequence" },
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
                name: "ix_live_solo_question_materials_challenge_id",
                table: "live_solo_question_materials",
                column: "challenge_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_competition_id_source_template",
                table: "live_solo_question_materials",
                columns: new[] { "competition_id", "source_template_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_created_by_user_id",
                table: "live_solo_question_materials",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_question_materials_source_template_id",
                table: "live_solo_question_materials",
                column: "source_template_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_actor_user_id",
                table: "live_solo_recording_decisions",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_media_session_id",
                table: "live_solo_recording_decisions",
                column: "media_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recording_decisions_recording_id_occurred_at",
                table: "live_solo_recording_decisions",
                columns: new[] { "recording_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_file_id",
                table: "live_solo_recordings",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_recordings_media_session_id_user_id_video_track_i",
                table: "live_solo_recordings",
                columns: new[] { "media_session_id", "user_id", "video_track_id", "chunk" },
                unique: true);

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
                name: "ix_live_solo_rescue_requests_actor_user_id",
                table: "live_solo_rescue_requests",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_judge_user_id",
                table: "live_solo_rescue_requests",
                column: "judge_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_match_id",
                table: "live_solo_rescue_requests",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_round_id_team_id_state",
                table: "live_solo_rescue_requests",
                columns: new[] { "round_id", "team_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_team_id",
                table: "live_solo_rescue_requests",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rescue_requests_user_id",
                table: "live_solo_rescue_requests",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_actor_user_id",
                table: "live_solo_result_corrections",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_competition_id",
                table: "live_solo_result_corrections",
                column: "competition_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_match_id_created_at",
                table: "live_solo_result_corrections",
                columns: new[] { "match_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_result_corrections_resolved_by_user_id",
                table: "live_solo_result_corrections",
                column: "resolved_by_user_id");

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
                name: "ix_live_solo_round_roster_members_team_id",
                table: "live_solo_round_roster_members",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_roster_members_user_id",
                table: "live_solo_round_roster_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_selection_audits_actor_user_id",
                table: "live_solo_round_selection_audits",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_round_selection_risks_round_id_selection_position",
                table: "live_solo_round_selection_risks",
                columns: new[] { "round_id", "selection_position" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_rounds_direction_draw_id",
                table: "live_solo_rounds",
                column: "direction_draw_id");

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
                name: "ix_live_solo_rounds_selected_by_user_id",
                table: "live_solo_rounds",
                column: "selected_by_user_id");

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

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_competition_id_expires_at",
                table: "live_solo_viewer_leases",
                columns: new[] { "competition_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_match_id",
                table: "live_solo_viewer_leases",
                column: "match_id");

            migrationBuilder.CreateIndex(
                name: "ix_live_solo_viewer_leases_user_id",
                table: "live_solo_viewer_leases",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_expires_at",
                table: "mfa_challenges",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_mfa_challenges_user_id_purpose",
                table: "mfa_challenges",
                columns: new[] { "user_id", "purpose" });

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
                name: "ix_oidc_mfa_amr_groups_sso_provider_id_position",
                table: "oidc_mfa_amr_groups",
                columns: new[] { "sso_provider_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_passkey_ceremonies_expires_at",
                table: "passkey_ceremonies",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_passkey_ceremonies_user_id_purpose",
                table: "passkey_ceremonies",
                columns: new[] { "user_id", "purpose" });

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
                column: "runtime_instance_id");

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
                name: "ix_questionnaire_active_sessions_participant_id_session_id",
                table: "questionnaire_active_sessions",
                columns: new[] { "participant_id", "session_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_active_sessions_session_id",
                table: "questionnaire_active_sessions",
                column: "session_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activities_owner_id",
                table: "questionnaire_activities",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activities_state_closes_at",
                table: "questionnaire_activities",
                columns: new[] { "state", "closes_at" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_fixed_questionnaires_activity_id_cha",
                table: "questionnaire_activity_fixed_questionnaires",
                columns: new[] { "activity_id", "challenge_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_fixed_questionnaires_challenge_id",
                table: "questionnaire_activity_fixed_questionnaires",
                column: "challenge_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_fixed_questionnaires_revision_id_cha",
                table: "questionnaire_activity_fixed_questionnaires",
                columns: new[] { "revision_id", "challenge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_operations_activity_id",
                table: "questionnaire_activity_operations",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_operations_actor_id",
                table: "questionnaire_activity_operations",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_operations_revision_id",
                table: "questionnaire_activity_operations",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_operations_session_id",
                table: "questionnaire_activity_operations",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_activity_pauses_activity_id_started_at",
                table: "questionnaire_activity_pauses",
                columns: new[] { "activity_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_answer_receipts_actor_id",
                table: "questionnaire_answer_receipts",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_assignments_revision_id_challenge_id",
                table: "questionnaire_assignments",
                columns: new[] { "revision_id", "challenge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_assignments_session_id_revision_id",
                table: "questionnaire_assignments",
                columns: new[] { "session_id", "revision_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_draw_candidates_revision_id",
                table: "questionnaire_draw_candidates",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_draw_candidates_source_id",
                table: "questionnaire_draw_candidates",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_draw_receipts_participant_id_session_id",
                table: "questionnaire_draw_receipts",
                columns: new[] { "participant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_draw_receipts_source_id",
                table: "questionnaire_draw_receipts",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_draw_rules_activity_id_order",
                table: "questionnaire_draw_rules",
                columns: new[] { "activity_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_files_file_id",
                table: "questionnaire_files",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_grading_options_revision_id_option_id",
                table: "questionnaire_grading_options",
                columns: new[] { "revision_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_grading_overrides_revision_id",
                table: "questionnaire_grading_overrides",
                column: "revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_issue_receipts_participant_id_session_id",
                table: "questionnaire_issue_receipts",
                columns: new[] { "participant_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_options_question_version_id_position",
                table: "questionnaire_options",
                columns: new[] { "question_version_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_options_question_version_id_revision_id_quest",
                table: "questionnaire_options",
                columns: new[] { "question_version_id", "revision_id", "question_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_participants_activity_id_user_id",
                table: "questionnaire_participants",
                columns: new[] { "activity_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_participants_user_id",
                table: "questionnaire_participants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_question_exposures_activity_id",
                table: "questionnaire_question_exposures",
                column: "activity_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_question_results_gameplay_fact_id_capability_",
                table: "questionnaire_question_results",
                columns: new[] { "gameplay_fact_id", "capability_kind" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_questions_revision_id_position",
                table: "questionnaire_questions",
                columns: new[] { "revision_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_questions_source_id",
                table: "questionnaire_questions",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_revisions_challenge_id_number",
                table: "questionnaire_revisions",
                columns: new[] { "challenge_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_options_item_id_order",
                table: "questionnaire_session_options",
                columns: new[] { "item_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_options_item_id_question_version_id",
                table: "questionnaire_session_options",
                columns: new[] { "item_id", "question_version_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_options_question_version_id_option_id",
                table: "questionnaire_session_options",
                columns: new[] { "question_version_id", "option_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_answered_by_id",
                table: "questionnaire_session_questions",
                column: "answered_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_assignment_id_session_id_qu",
                table: "questionnaire_session_questions",
                columns: new[] { "assignment_id", "session_id", "questionnaire_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_revision_id_questionnaire_r",
                table: "questionnaire_session_questions",
                columns: new[] { "revision_id", "questionnaire_revision_id", "questionnaire_question_id" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_rule_id",
                table: "questionnaire_session_questions",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_session_id_order",
                table: "questionnaire_session_questions",
                columns: new[] { "session_id", "order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_session_questions_session_id_revision_id",
                table: "questionnaire_session_questions",
                columns: new[] { "session_id", "revision_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_sessions_participant_id_attempt",
                table: "questionnaire_sessions",
                columns: new[] { "participant_id", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_sessions_state_opened_at",
                table: "questionnaire_sessions",
                columns: new[] { "state", "opened_at" });

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_sessions_submitted_by_id",
                table: "questionnaire_sessions",
                column: "submitted_by_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_expires_at",
                table: "request_admission_leases",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_key_hash_expires_at",
                table: "request_admission_leases",
                columns: new[] { "key_hash", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_leases_lease_id",
                table: "request_admission_leases",
                column: "lease_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_admission_windows_expires_at",
                table: "request_admission_windows",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_runtime_capacity_allocations_resource_domain",
                table: "runtime_capacity_allocations",
                column: "resource_domain");

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
                name: "ix_runtime_receipt_service_ports_service_id_container_port",
                table: "runtime_receipt_service_ports",
                columns: new[] { "service_id", "container_port" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_services_runtime_instance_id_name",
                table: "runtime_receipt_services",
                columns: new[] { "runtime_instance_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_runtime_receipt_virtual_machines_runtime_instance_id",
                table: "runtime_receipt_virtual_machines",
                column: "runtime_instance_id");

            migrationBuilder.CreateIndex(
                name: "ix_sso_flows_correlation_hash",
                table: "sso_flows",
                column: "correlation_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sso_flows_expires_at",
                table: "sso_flows",
                column: "expires_at");

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
                columns: new[] { "competition_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_members_user_id",
                table: "team_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_badge_states_badge_id",
                table: "team_progression_badge_states",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_badge_states_competition_id_badge_id",
                table: "team_progression_badge_states",
                columns: new[] { "competition_id", "badge_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_states_competition_id_team_id",
                table: "team_progression_node_states",
                columns: new[] { "competition_id", "team_id" });

            migrationBuilder.CreateIndex(
                name: "ix_team_progression_node_states_node_id",
                table: "team_progression_node_states",
                column: "node_id");

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
                name: "ix_template_capability_tests_actor_user_id",
                table: "template_capability_tests",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_capability_tests_execution_id",
                table: "template_capability_tests",
                column: "execution_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_template_capability_tests_file_id",
                table: "template_capability_tests",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_capability_tests_state_deadline",
                table: "template_capability_tests",
                columns: new[] { "state", "deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_template_check_flag_contexts_flag_id",
                table: "template_check_flag_contexts",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_check_probes_flag_id",
                table: "template_check_probes",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_template_check_requirements_flag_id",
                table: "template_check_requirements",
                column: "flag_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_badge_id",
                table: "user_badge_grants",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_competition_id_user_id_active",
                table: "user_badge_grants",
                columns: new[] { "competition_id", "user_id", "active" });

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_grants_user_id",
                table: "user_badge_grants",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_badge_id",
                table: "user_badge_transitions",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_competition_id_team_id_occurred_at",
                table: "user_badge_transitions",
                columns: new[] { "competition_id", "team_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_team_id",
                table: "user_badge_transitions",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_badge_transitions_user_id_badge_id_occurred_at",
                table: "user_badge_transitions",
                columns: new[] { "user_id", "badge_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_mfa_recovery_codes_user_id_batch_id_code_sha256",
                table: "user_mfa_recovery_codes",
                columns: new[] { "user_id", "batch_id", "code_sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_credential_id",
                table: "user_passkeys",
                column: "credential_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_passkeys_user_id_relying_party_id",
                table: "user_passkeys",
                columns: new[] { "user_id", "relying_party_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_totp_credentials_user_id",
                table: "user_totp_credentials",
                column: "user_id",
                unique: true);

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

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_active_team_slots_live_solo_matches_match_id",
                table: "live_solo_active_team_slots",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_adjudications_live_solo_matches_match_id",
                table: "live_solo_adjudications",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_adjudications_live_solo_rounds_round_id",
                table: "live_solo_adjudications",
                column: "round_id",
                principalTable: "live_solo_rounds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_attachment_assignments_live_solo_round_questions_",
                table: "live_solo_attachment_assignments",
                column: "round_question_id",
                principalTable: "live_solo_round_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_correction_matches_live_solo_matches_match_id",
                table: "live_solo_correction_matches",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_correction_matches_live_solo_matches_replacement_",
                table: "live_solo_correction_matches",
                column: "replacement_match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_correction_matches_live_solo_result_corrections_c",
                table: "live_solo_correction_matches",
                column: "correction_id",
                principalTable: "live_solo_result_corrections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_download_evidence_live_solo_round_questions_round",
                table: "live_solo_download_evidence",
                column: "round_question_id",
                principalTable: "live_solo_round_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_fix_targets_live_solo_round_questions_round_quest",
                table: "live_solo_fix_targets",
                column: "round_question_id",
                principalTable: "live_solo_round_questions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_match_directions_live_solo_matches_match_id",
                table: "live_solo_match_directions",
                column: "match_id",
                principalTable: "live_solo_matches",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

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
                name: "fk_live_solo_matches_live_solo_media_sessions_current_media_se",
                table: "live_solo_matches",
                column: "current_media_session_id",
                principalTable: "live_solo_media_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_result_correction_pending_corre",
                table: "live_solo_matches",
                column: "pending_correction_id",
                principalTable: "live_solo_result_corrections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_matches_live_solo_rounds_current_round_id",
                table: "live_solo_matches",
                column: "current_round_id",
                principalTable: "live_solo_rounds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_media_grants_live_solo_media_sessions_media_sessi",
                table: "live_solo_media_grants",
                column: "media_session_id",
                principalTable: "live_solo_media_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_media_participants_live_solo_media_sessions_media",
                table: "live_solo_media_participants",
                column: "media_session_id",
                principalTable: "live_solo_media_sessions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_live_solo_media_sessions_live_solo_program_captures_current",
                table: "live_solo_media_sessions",
                columns: new[] { "current_program_capture_id", "id" },
                principalTable: "live_solo_program_captures",
                principalColumns: new[] { "id", "media_session_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_challenges_users_owner_id",
                table: "challenges");

            migrationBuilder.DropForeignKey(
                name: "fk_competitions_users_owner_id",
                table: "competitions");

            migrationBuilder.DropForeignKey(
                name: "fk_gameplay_facts_users_actor_user_id",
                table: "gameplay_facts");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_users_deleted_by_user_id",
                table: "live_solo_matches");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_result_corrections_users_actor_user_id",
                table: "live_solo_result_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_result_corrections_users_resolved_by_user_id",
                table: "live_solo_result_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_users_selected_by_user_id",
                table: "live_solo_rounds");

            migrationBuilder.DropForeignKey(
                name: "fk_questionnaire_activities_users_owner_id",
                table: "questionnaire_activities");

            migrationBuilder.DropForeignKey(
                name: "fk_questionnaire_participants_users_user_id",
                table: "questionnaire_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_questionnaire_sessions_users_submitted_by_id",
                table: "questionnaire_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_gameplay_facts_winning_gameplay_fact_id",
                table: "live_solo_rounds");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_teams_winner_team_id",
                table: "live_solo_matches");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_teams_winner_team_id",
                table: "live_solo_rounds");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_matches_competitions_competition_id",
                table: "live_solo_matches");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_question_groups_competitions_competition_id",
                table: "live_solo_question_groups");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_result_corrections_competitions_competition_id",
                table: "live_solo_result_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_match_directions_live_solo_matches_match_id",
                table: "live_solo_match_directions");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_media_sessions_live_solo_matches_match_id",
                table: "live_solo_media_sessions");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_result_corrections_live_solo_matches_match_id",
                table: "live_solo_result_corrections");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_rounds_live_solo_matches_match_id",
                table: "live_solo_rounds");

            migrationBuilder.DropForeignKey(
                name: "fk_live_solo_program_captures_live_solo_media_sessions_media_s",
                table: "live_solo_program_captures");

            migrationBuilder.DropTable(
                name: "account_tokens");

            migrationBuilder.DropTable(
                name: "active_runtime_slots");

            migrationBuilder.DropTable(
                name: "active_team_memberships");

            migrationBuilder.DropTable(
                name: "awd_capture_identities");

            migrationBuilder.DropTable(
                name: "awd_check_flag_evidence");

            migrationBuilder.DropTable(
                name: "awd_competition_tie_breaks");

            migrationBuilder.DropTable(
                name: "awd_round_team_eligibility");

            migrationBuilder.DropTable(
                name: "awd_round_tie_breaks");

            migrationBuilder.DropTable(
                name: "awd_scoring_rule_changes");

            migrationBuilder.DropTable(
                name: "awd_store_rules");

            migrationBuilder.DropTable(
                name: "break_flag_matches");

            migrationBuilder.DropTable(
                name: "challenge_check_command");

            migrationBuilder.DropTable(
                name: "challenge_check_environment");

            migrationBuilder.DropTable(
                name: "challenge_check_requirements");

            migrationBuilder.DropTable(
                name: "challenge_fix_command");

            migrationBuilder.DropTable(
                name: "challenge_flag_environment_targets");

            migrationBuilder.DropTable(
                name: "challenge_flag_stores");

            migrationBuilder.DropTable(
                name: "challenge_managers");

            migrationBuilder.DropTable(
                name: "challenge_runtime_service_commands");

            migrationBuilder.DropTable(
                name: "challenge_runtime_service_environment");

            migrationBuilder.DropTable(
                name: "challenge_runtime_service_internal_ports");

            migrationBuilder.DropTable(
                name: "challenge_runtime_url_bindings");

            migrationBuilder.DropTable(
                name: "challenge_sources");

            migrationBuilder.DropTable(
                name: "check_flag_contexts");

            migrationBuilder.DropTable(
                name: "check_probes");

            migrationBuilder.DropTable(
                name: "command_receipt_gameplay_fact_results");

            migrationBuilder.DropTable(
                name: "competition_blood_announcements");

            migrationBuilder.DropTable(
                name: "competition_blood_rewards");

            migrationBuilder.DropTable(
                name: "competition_challenge_blood_rewards");

            migrationBuilder.DropTable(
                name: "competition_challenge_hints");

            migrationBuilder.DropTable(
                name: "competition_challenge_tags");

            migrationBuilder.DropTable(
                name: "competition_challenge_writeup_unlocks");

            migrationBuilder.DropTable(
                name: "competition_collaborators");

            migrationBuilder.DropTable(
                name: "competition_event_track_keys");

            migrationBuilder.DropTable(
                name: "competition_progression_edges");

            migrationBuilder.DropTable(
                name: "competition_score_presets");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_event_items");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_streams");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_work_items");

            migrationBuilder.DropTable(
                name: "competition_tracks");

            migrationBuilder.DropTable(
                name: "competition_webhook_deliveries");

            migrationBuilder.DropTable(
                name: "competition_webhook_frozen_projections");

            migrationBuilder.DropTable(
                name: "competition_webhook_outbox_events");

            migrationBuilder.DropTable(
                name: "competition_webhook_targets");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "external_identities");

            migrationBuilder.DropTable(
                name: "fix_execution_details");

            migrationBuilder.DropTable(
                name: "flag_acquisition_evidence");

            migrationBuilder.DropTable(
                name: "human_verification_turnstile_hostnames");

            migrationBuilder.DropTable(
                name: "live_solo_active_team_slots");

            migrationBuilder.DropTable(
                name: "live_solo_adjudications");

            migrationBuilder.DropTable(
                name: "live_solo_attachment_assignments");

            migrationBuilder.DropTable(
                name: "live_solo_correction_matches");

            migrationBuilder.DropTable(
                name: "live_solo_direction_presets");

            migrationBuilder.DropTable(
                name: "live_solo_download_evidence");

            migrationBuilder.DropTable(
                name: "live_solo_fix_targets");

            migrationBuilder.DropTable(
                name: "live_solo_match_slots");

            migrationBuilder.DropTable(
                name: "live_solo_media_grants");

            migrationBuilder.DropTable(
                name: "live_solo_media_participants");

            migrationBuilder.DropTable(
                name: "live_solo_opened_attachments");

            migrationBuilder.DropTable(
                name: "live_solo_opened_hints");

            migrationBuilder.DropTable(
                name: "live_solo_opened_tags");

            migrationBuilder.DropTable(
                name: "live_solo_pause_intervals");

            migrationBuilder.DropTable(
                name: "live_solo_program_decisions");

            migrationBuilder.DropTable(
                name: "live_solo_program_frame_questions");

            migrationBuilder.DropTable(
                name: "live_solo_program_segments");

            migrationBuilder.DropTable(
                name: "live_solo_question_exposures");

            migrationBuilder.DropTable(
                name: "live_solo_question_group_items");

            migrationBuilder.DropTable(
                name: "live_solo_question_materials");

            migrationBuilder.DropTable(
                name: "live_solo_recording_decisions");

            migrationBuilder.DropTable(
                name: "live_solo_recordings");

            migrationBuilder.DropTable(
                name: "live_solo_rescue_requests");

            migrationBuilder.DropTable(
                name: "live_solo_roster_members");

            migrationBuilder.DropTable(
                name: "live_solo_round_roster_members");

            migrationBuilder.DropTable(
                name: "live_solo_round_selection_risks");

            migrationBuilder.DropTable(
                name: "live_solo_runtime_bindings");

            migrationBuilder.DropTable(
                name: "live_solo_stage_rules");

            migrationBuilder.DropTable(
                name: "live_solo_submissions");

            migrationBuilder.DropTable(
                name: "live_solo_tournament_stages");

            migrationBuilder.DropTable(
                name: "live_solo_viewer_leases");

            migrationBuilder.DropTable(
                name: "mfa_challenges");

            migrationBuilder.DropTable(
                name: "notification_reference_counts");

            migrationBuilder.DropTable(
                name: "oidc_mfa_acr_values");

            migrationBuilder.DropTable(
                name: "oidc_mfa_amr_values");

            migrationBuilder.DropTable(
                name: "passkey_ceremonies");

            migrationBuilder.DropTable(
                name: "patch_uploads");

            migrationBuilder.DropTable(
                name: "questionnaire_active_sessions");

            migrationBuilder.DropTable(
                name: "questionnaire_activity_fixed_questionnaires");

            migrationBuilder.DropTable(
                name: "questionnaire_activity_operations");

            migrationBuilder.DropTable(
                name: "questionnaire_activity_pauses");

            migrationBuilder.DropTable(
                name: "questionnaire_answer_receipts");

            migrationBuilder.DropTable(
                name: "questionnaire_classifications");

            migrationBuilder.DropTable(
                name: "questionnaire_draw_candidates");

            migrationBuilder.DropTable(
                name: "questionnaire_draw_receipts");

            migrationBuilder.DropTable(
                name: "questionnaire_draw_rule_filters");

            migrationBuilder.DropTable(
                name: "questionnaire_files");

            migrationBuilder.DropTable(
                name: "questionnaire_grading_options");

            migrationBuilder.DropTable(
                name: "questionnaire_issue_receipts");

            migrationBuilder.DropTable(
                name: "questionnaire_question_exposures");

            migrationBuilder.DropTable(
                name: "questionnaire_question_results");

            migrationBuilder.DropTable(
                name: "questionnaire_session_options");

            migrationBuilder.DropTable(
                name: "request_admission_leases");

            migrationBuilder.DropTable(
                name: "request_admission_windows");

            migrationBuilder.DropTable(
                name: "runtime_access_endpoints");

            migrationBuilder.DropTable(
                name: "runtime_capacity_allocations");

            migrationBuilder.DropTable(
                name: "runtime_capacity_ledger");

            migrationBuilder.DropTable(
                name: "runtime_published_ports");

            migrationBuilder.DropTable(
                name: "runtime_receipt_service_ports");

            migrationBuilder.DropTable(
                name: "runtime_receipt_virtual_machines");

            migrationBuilder.DropTable(
                name: "sso_flows");

            migrationBuilder.DropTable(
                name: "sso_provider_allowed_hosts");

            migrationBuilder.DropTable(
                name: "sso_provider_oidc_scopes");

            migrationBuilder.DropTable(
                name: "staff_webhook_category");

            migrationBuilder.DropTable(
                name: "team_captains");

            migrationBuilder.DropTable(
                name: "team_progression_badge_states");

            migrationBuilder.DropTable(
                name: "team_progression_node_states");

            migrationBuilder.DropTable(
                name: "template_check_flag_contexts");

            migrationBuilder.DropTable(
                name: "template_check_probes");

            migrationBuilder.DropTable(
                name: "template_check_requirements");

            migrationBuilder.DropTable(
                name: "user_badge_grants");

            migrationBuilder.DropTable(
                name: "user_badge_transitions");

            migrationBuilder.DropTable(
                name: "user_mfa_recovery_codes");

            migrationBuilder.DropTable(
                name: "user_passkey_transports");

            migrationBuilder.DropTable(
                name: "user_totp_credentials");

            migrationBuilder.DropTable(
                name: "awd_flag_issues");

            migrationBuilder.DropTable(
                name: "awd_fact_details");

            migrationBuilder.DropTable(
                name: "challenge_capabilities");

            migrationBuilder.DropTable(
                name: "challenge_runtime_services");

            migrationBuilder.DropTable(
                name: "command_receipts");

            migrationBuilder.DropTable(
                name: "competition_challenge_rules");

            migrationBuilder.DropTable(
                name: "competition_challenge_writeup_versions");

            migrationBuilder.DropTable(
                name: "competition_events");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_events");

            migrationBuilder.DropTable(
                name: "challenge_attachments");

            migrationBuilder.DropTable(
                name: "live_solo_opened_contents");

            migrationBuilder.DropTable(
                name: "live_solo_program_frames");

            migrationBuilder.DropTable(
                name: "live_solo_round_selection_audits");

            migrationBuilder.DropTable(
                name: "competition_mode_configurations");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "oidc_mfa_amr_groups");

            migrationBuilder.DropTable(
                name: "questionnaire_grading_overrides");

            migrationBuilder.DropTable(
                name: "capability_outcomes");

            migrationBuilder.DropTable(
                name: "questionnaire_options");

            migrationBuilder.DropTable(
                name: "questionnaire_session_questions");

            migrationBuilder.DropTable(
                name: "runtime_receipt_services");

            migrationBuilder.DropTable(
                name: "competition_staff_webhook_targets");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "competition_progression_nodes");

            migrationBuilder.DropTable(
                name: "template_capability_tests");

            migrationBuilder.DropTable(
                name: "user_passkeys");

            migrationBuilder.DropTable(
                name: "awd_flag_keys");

            migrationBuilder.DropTable(
                name: "awd_round_store_budgets");

            migrationBuilder.DropTable(
                name: "challenge_flags");

            migrationBuilder.DropTable(
                name: "challenge_runtime_templates");

            migrationBuilder.DropTable(
                name: "competition_challenge_writeups");

            migrationBuilder.DropTable(
                name: "live_solo_round_questions");

            migrationBuilder.DropTable(
                name: "sso_providers");

            migrationBuilder.DropTable(
                name: "questionnaire_draw_rules");

            migrationBuilder.DropTable(
                name: "questionnaire_questions");

            migrationBuilder.DropTable(
                name: "runtime_receipts");

            migrationBuilder.DropTable(
                name: "competition_badges");

            migrationBuilder.DropTable(
                name: "competition_progressions");

            migrationBuilder.DropTable(
                name: "awd_round_rules");

            migrationBuilder.DropTable(
                name: "challenge_definitions");

            migrationBuilder.DropTable(
                name: "platform_settings");

            migrationBuilder.DropTable(
                name: "questionnaire_sources");

            migrationBuilder.DropTable(
                name: "runtime_instances");

            migrationBuilder.DropTable(
                name: "awd_rounds");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "gameplay_facts");

            migrationBuilder.DropTable(
                name: "competition_challenges");

            migrationBuilder.DropTable(
                name: "questionnaire_assignments");

            migrationBuilder.DropTable(
                name: "competition_directions");

            migrationBuilder.DropTable(
                name: "questionnaire_revisions");

            migrationBuilder.DropTable(
                name: "questionnaire_sessions");

            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "questionnaire_participants");

            migrationBuilder.DropTable(
                name: "questionnaire_activities");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "competitions");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "live_solo_matches");

            migrationBuilder.DropTable(
                name: "live_solo_result_corrections");

            migrationBuilder.DropTable(
                name: "live_solo_rounds");

            migrationBuilder.DropTable(
                name: "live_solo_match_directions");

            migrationBuilder.DropTable(
                name: "live_solo_question_groups");

            migrationBuilder.DropTable(
                name: "live_solo_media_sessions");

            migrationBuilder.DropTable(
                name: "live_solo_program_captures");
        }
    }
}

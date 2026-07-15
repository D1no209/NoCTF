using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoCTF.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQqBotBroadcastPlugin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompetitionQqBotSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    AllowMessages = table.Column<bool>(type: "boolean", nullable: false),
                    AllowManualNotifications = table.Column<bool>(type: "boolean", nullable: false),
                    StopNormalEventsAfterFinished = table.Column<bool>(type: "boolean", nullable: false),
                    MentionAll = table.Column<bool>(type: "boolean", nullable: false),
                    ShowTeamName = table.Column<bool>(type: "boolean", nullable: false),
                    ShowUserName = table.Column<bool>(type: "boolean", nullable: false),
                    ShowChallengeCategory = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeCompetitionLink = table.Column<bool>(type: "boolean", nullable: false),
                    IncludeChallengeLink = table.Column<bool>(type: "boolean", nullable: false),
                    HidePenaltyDetails = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionQqBotSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotSettings_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QqBotAgents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    PublicKeyPem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    PreviousPublicKeyPem = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    PreviousKeyValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastGroupSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    QqOnline = table.Column<bool>(type: "boolean", nullable: false),
                    BotUin = table.Column<long>(type: "bigint", nullable: true),
                    BotNickname = table.Column<string>(type: "text", nullable: true),
                    ImplementationName = table.Column<string>(type: "text", nullable: true),
                    ImplementationVersion = table.Column<string>(type: "text", nullable: true),
                    MilkyVersion = table.Column<string>(type: "text", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastErrorSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotAgents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QqBotEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    TargetGroupIdsJson = table.Column<string>(type: "text", nullable: true),
                    RequestedTemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentDeliveryId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastErrorSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpandedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QqBotEvents_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QqBotGlobalSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LongPollSeconds = table.Column<int>(type: "integer", nullable: false),
                    DeliveryLeaseSeconds = table.Column<int>(type: "integer", nullable: false),
                    MaxDeliveryAttempts = table.Column<int>(type: "integer", nullable: false),
                    MaxMessageLength = table.Column<int>(type: "integer", nullable: false),
                    MaxPendingDeliveries = table.Column<int>(type: "integer", nullable: false),
                    GroupCooldownMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    CompetitionCooldownMilliseconds = table.Column<int>(type: "integer", nullable: false),
                    ManualNotificationCooldownSeconds = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotGlobalSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "QqBotTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Content = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QqBotTemplates_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QqBotGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<long>(type: "bigint", nullable: false),
                    GroupName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsPresent = table.Column<bool>(type: "boolean", nullable: false),
                    IsAuthorized = table.Column<bool>(type: "boolean", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QqBotGroups_QqBotAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "QqBotAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionQqBotEventRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionQqBotEventRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotEventRules_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotEventRules_QqBotTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "QqBotTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CompetitionQqBotGroupBindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    EventTypesJson = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompetitionQqBotGroupBindings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotGroupBindings_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotGroupBindings_QqBotAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "QqBotAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompetitionQqBotGroupBindings_QqBotGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "QqBotGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QqBotDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    QqGroupId = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    RenderedText = table.Column<string>(type: "text", nullable: false),
                    RenderedSegmentsJson = table.Column<string>(type: "text", nullable: false),
                    MessageDigest = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(768)", maxLength: 768, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false),
                    AvailableAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseToken = table.Column<Guid>(type: "uuid", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemoteMessageSequence = table.Column<long>(type: "bigint", nullable: true),
                    RemoteSentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    LastErrorSummary = table.Column<string>(type: "text", nullable: true),
                    LastErrorRetryable = table.Column<bool>(type: "boolean", nullable: true),
                    SafeRemoteSummary = table.Column<string>(type: "text", nullable: true),
                    RetriedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QqBotDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QqBotDeliveries_Competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "Competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QqBotDeliveries_QqBotAgents_AgentId",
                        column: x => x.AgentId,
                        principalTable: "QqBotAgents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QqBotDeliveries_QqBotEvents_EventId",
                        column: x => x.EventId,
                        principalTable: "QqBotEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QqBotDeliveries_QqBotGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "QqBotGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionQqBotEventRules_TemplateId",
                table: "CompetitionQqBotEventRules",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "ux_competitionqqboteventrules_competition_event",
                table: "CompetitionQqBotEventRules",
                columns: new[] { "CompetitionId", "EventType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionQqBotGroupBindings_AgentId",
                table: "CompetitionQqBotGroupBindings",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_CompetitionQqBotGroupBindings_GroupId",
                table: "CompetitionQqBotGroupBindings",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "ux_competitionqqbotgroupbindings_competition_group",
                table: "CompetitionQqBotGroupBindings",
                columns: new[] { "CompetitionId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_competitionqqbotsettings_competition",
                table: "CompetitionQqBotSettings",
                column: "CompetitionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qqbotdeliveries_agent_dispatch",
                table: "QqBotDeliveries",
                columns: new[] { "AgentId", "Status", "AvailableAt", "LockedUntil" });

            migrationBuilder.CreateIndex(
                name: "ix_qqbotdeliveries_competition_created",
                table: "QqBotDeliveries",
                columns: new[] { "CompetitionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QqBotDeliveries_EventId",
                table: "QqBotDeliveries",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_QqBotDeliveries_GroupId",
                table: "QqBotDeliveries",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "ux_qqbotdeliveries_idempotency",
                table: "QqBotDeliveries",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_qqbotevents_competition_idempotency",
                table: "QqBotEvents",
                columns: new[] { "CompetitionId", "IdempotencyKey" });

            migrationBuilder.CreateIndex(
                name: "ix_qqbotevents_status_created",
                table: "QqBotEvents",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "ux_qqbotgroups_agent_group",
                table: "QqBotGroups",
                columns: new[] { "AgentId", "GroupId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_qqbottemplates_scope_event_name",
                table: "QqBotTemplates",
                columns: new[] { "CompetitionId", "EventType", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompetitionQqBotEventRules");

            migrationBuilder.DropTable(
                name: "CompetitionQqBotGroupBindings");

            migrationBuilder.DropTable(
                name: "CompetitionQqBotSettings");

            migrationBuilder.DropTable(
                name: "QqBotDeliveries");

            migrationBuilder.DropTable(
                name: "QqBotGlobalSettings");

            migrationBuilder.DropTable(
                name: "QqBotTemplates");

            migrationBuilder.DropTable(
                name: "QqBotEvents");

            migrationBuilder.DropTable(
                name: "QqBotGroups");

            migrationBuilder.DropTable(
                name: "QqBotAgents");
        }
    }
}

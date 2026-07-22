using System;
using Microsoft.EntityFrameworkCore.Migrations;

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
                name: "audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "text", nullable: false),
                    SubjectType = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    MetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Direction = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Deletion_IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Deletion_DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deletion_DeletedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "competitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    ConfigurationRevision = table.Column<int>(type: "integer", nullable: false),
                    ConfigurationUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TeamRegistrationAutoApprove = table.Column<bool>(type: "boolean", nullable: false),
                    MaxTeamMembers = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Deletion_IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Deletion_DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deletion_DeletedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "text", nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "runtime_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ClaimToken = table.Column<Guid>(type: "uuid", nullable: false),
                    ErrorCode = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_runtime_operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    InvitationToken = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                    RegistrationStatus = table.Column<int>(type: "integer", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ban_IsBanned = table.Column<bool>(type: "boolean", nullable: false),
                    Ban_BannedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Ban_BannedById = table.Column<Guid>(type: "uuid", nullable: true),
                    Ban_Reason = table.Column<string>(type: "text", nullable: true),
                    Deletion_IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Deletion_DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deletion_DeletedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teams", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NormalizedUserName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    TokenVersion = table.Column<int>(type: "integer", nullable: false),
                    EmailVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "challenge_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_challenge_attachments_challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseScore = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Deletion_IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    Deletion_DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Deletion_DeletedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_challenges_challenges_ChallengeId",
                        column: x => x.ChallengeId,
                        principalTable: "challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_competition_challenges_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_collaborators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_collaborators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_collaborators_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "competition_lifecycle_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    From = table.Column<int>(type: "integer", nullable: false),
                    To = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Automatic = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_lifecycle_audits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_lifecycle_audits_competitions_CompetitionId",
                        column: x => x.CompetitionId,
                        principalTable: "competitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "team_members",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberOrder = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_members", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_members_teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "challenge_flags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChallengeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Flag = table.Column<string>(type: "text", nullable: false),
                    ValidStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ValidEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    InjectionClaimToken = table.Column<Guid>(type: "uuid", nullable: true),
                    InjectionClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_flags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_challenge_flags_competition_challenges_CompetitionChallenge~",
                        column: x => x.CompetitionChallengeId,
                        principalTable: "competition_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "challenge_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Receipt = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EntryUrl = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_challenge_instances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_challenge_instances_competition_challenges_CompetitionChall~",
                        column: x => x.CompetitionChallengeId,
                        principalTable: "competition_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "competition_challenge_hints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Cost = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_competition_challenge_hints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_competition_challenge_hints_competition_challenges_Competit~",
                        column: x => x.CompetitionChallengeId,
                        principalTable: "competition_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fix_submission_records",
                columns: table => new
                {
                    UploadId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ObjectKey = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ObjectMetadata = table.Column<string>(type: "jsonb", nullable: true),
                    VerificationStatus = table.Column<int>(type: "integer", nullable: false),
                    FailureCategory = table.Column<int>(type: "integer", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifierVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fix_submission_records", x => x.UploadId);
                });

            migrationBuilder.CreateTable(
                name: "scoring_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChallengeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<int>(type: "integer", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessedWorkerId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EvaluatorVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedById = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scoring_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_scoring_events_competition_challenges_CompetitionChallengeId",
                        column: x => x.CompetitionChallengeId,
                        principalTable: "competition_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompetitionChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FlagHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FlagLength = table.Column<int>(type: "integer", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubjectTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    VictimTeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    StageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ChallengeInstanceId = table.Column<Guid>(type: "uuid", nullable: true),
                    ControlIntervalSeconds = table.Column<long>(type: "bigint", nullable: true),
                    CheckerPlatformError = table.Column<bool>(type: "boolean", nullable: false),
                    ScoringEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProcessingVersion = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_submissions_competition_challenges_CompetitionChallengeId",
                        column: x => x.CompetitionChallengeId,
                        principalTable: "competition_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_submissions_scoring_events_ScoringEventId",
                        column: x => x.ScoringEventId,
                        principalTable: "scoring_events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_entries_CompetitionId_OccurredAt",
                table: "audit_entries",
                columns: new[] { "CompetitionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_challenge_attachments_ChallengeId",
                table: "challenge_attachments",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_attachments_ObjectKey",
                table: "challenge_attachments",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_challenge_flags_CompetitionChallengeId",
                table: "challenge_flags",
                column: "CompetitionChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_flags_CompetitionId_CompetitionChallengeId_TeamI~1",
                table: "challenge_flags",
                columns: new[] { "CompetitionId", "CompetitionChallengeId", "TeamId", "StageId", "ChallengeInstanceId", "ValidStart", "ValidEnd" });

            migrationBuilder.CreateIndex(
                name: "IX_challenge_flags_CompetitionId_CompetitionChallengeId_TeamId~",
                table: "challenge_flags",
                columns: new[] { "CompetitionId", "CompetitionChallengeId", "TeamId", "ValidStart" },
                unique: true,
                filter: "\"TeamId\" IS NOT NULL AND \"StageId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_instances_CompetitionChallengeId",
                table: "challenge_instances",
                column: "CompetitionChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_challenge_instances_CompetitionId_TeamId_CompetitionChallen~",
                table: "challenge_instances",
                columns: new[] { "CompetitionId", "TeamId", "CompetitionChallengeId" });

            migrationBuilder.CreateIndex(
                name: "IX_competition_challenge_hints_CompetitionChallengeId",
                table: "competition_challenge_hints",
                column: "CompetitionChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_competition_challenges_ChallengeId",
                table: "competition_challenges",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_competition_challenges_CompetitionId_ChallengeId",
                table: "competition_challenges",
                columns: new[] { "CompetitionId", "ChallengeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_competition_challenges_CompetitionId_Order",
                table: "competition_challenges",
                columns: new[] { "CompetitionId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_competition_collaborators_CompetitionId_UserId",
                table: "competition_collaborators",
                columns: new[] { "CompetitionId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_competition_lifecycle_audits_CompetitionId_OccurredAt",
                table: "competition_lifecycle_audits",
                columns: new[] { "CompetitionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_competitions_Status_StartTime",
                table: "competitions",
                columns: new[] { "Status", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_fix_submission_records_CompetitionId_TeamId_CompetitionChal~",
                table: "fix_submission_records",
                columns: new[] { "CompetitionId", "TeamId", "CompetitionChallengeId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_fix_submission_records_SubmissionId",
                table: "fix_submission_records",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_IsRead_CreatedAt",
                table: "notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_runtime_operations_CompetitionId_OperationKey",
                table: "runtime_operations",
                columns: new[] { "CompetitionId", "OperationKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_runtime_operations_CompetitionId_Status",
                table: "runtime_operations",
                columns: new[] { "CompetitionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_scoring_events_CompetitionChallengeId",
                table: "scoring_events",
                column: "CompetitionChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_scoring_events_CompetitionId_Kind_SourceKey",
                table: "scoring_events",
                columns: new[] { "CompetitionId", "Kind", "SourceKey" },
                unique: true,
                filter: "\"IsDeleted\" = FALSE AND \"SourceKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_scoring_events_CompetitionId_OccurredAt_Id",
                table: "scoring_events",
                columns: new[] { "CompetitionId", "OccurredAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_scoring_events_CompetitionId_TeamId_CompetitionChallengeId_~",
                table: "scoring_events",
                columns: new[] { "CompetitionId", "TeamId", "CompetitionChallengeId", "Kind", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_scoring_events_SubmissionId",
                table: "scoring_events",
                column: "SubmissionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_CompetitionChallengeId",
                table: "submissions",
                column: "CompetitionChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_submissions_CompetitionId_IdempotencyKey",
                table: "submissions",
                columns: new[] { "CompetitionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_submissions_CompetitionId_TeamId_CompetitionChallengeId_Kin~",
                table: "submissions",
                columns: new[] { "CompetitionId", "TeamId", "CompetitionChallengeId", "Kind", "ReceivedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_submissions_ScoringEventId",
                table: "submissions",
                column: "ScoringEventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_members_TeamId_MemberOrder",
                table: "team_members",
                columns: new[] { "TeamId", "MemberOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_team_members_TeamId_UserId",
                table: "team_members",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_CompetitionId_Name",
                table: "teams",
                columns: new[] { "CompetitionId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_teams_CompetitionId_RegistrationStatus",
                table: "teams",
                columns: new[] { "CompetitionId", "RegistrationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_teams_InvitationToken",
                table: "teams",
                column: "InvitationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedEmail",
                table: "users",
                column: "NormalizedEmail",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedUserName",
                table: "users",
                column: "NormalizedUserName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_fix_submission_records_submissions_SubmissionId",
                table: "fix_submission_records",
                column: "SubmissionId",
                principalTable: "submissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_scoring_events_submissions_SubmissionId",
                table: "scoring_events",
                column: "SubmissionId",
                principalTable: "submissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_competition_challenges_challenges_ChallengeId",
                table: "competition_challenges");

            migrationBuilder.DropForeignKey(
                name: "FK_scoring_events_competition_challenges_CompetitionChallengeId",
                table: "scoring_events");

            migrationBuilder.DropForeignKey(
                name: "FK_submissions_competition_challenges_CompetitionChallengeId",
                table: "submissions");

            migrationBuilder.DropForeignKey(
                name: "FK_scoring_events_submissions_SubmissionId",
                table: "scoring_events");

            migrationBuilder.DropTable(
                name: "audit_entries");

            migrationBuilder.DropTable(
                name: "challenge_attachments");

            migrationBuilder.DropTable(
                name: "challenge_flags");

            migrationBuilder.DropTable(
                name: "challenge_instances");

            migrationBuilder.DropTable(
                name: "competition_challenge_hints");

            migrationBuilder.DropTable(
                name: "competition_collaborators");

            migrationBuilder.DropTable(
                name: "competition_lifecycle_audits");

            migrationBuilder.DropTable(
                name: "fix_submission_records");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "runtime_operations");

            migrationBuilder.DropTable(
                name: "team_members");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "challenges");

            migrationBuilder.DropTable(
                name: "competition_challenges");

            migrationBuilder.DropTable(
                name: "competitions");

            migrationBuilder.DropTable(
                name: "submissions");

            migrationBuilder.DropTable(
                name: "scoring_events");
        }
    }
}

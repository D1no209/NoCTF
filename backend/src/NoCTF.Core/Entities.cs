using System.ComponentModel.DataAnnotations.Schema;
using NoCTF.PluginBase;

namespace NoCTF.Core;

/// <summary>Status of an AWD round.</summary>
public enum AwdRoundStatus
{
    Idle,
    Running,
    Paused,
    Finished
}

public static class UserInputLimits
{
    public const int UserNameMinLength = 3;
    public const int UserNameMaxLength = 64;
    public const int EmailMaxLength = 254;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
}

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public int TokenVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class Team : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public Guid CaptainId { get; set; }
    public string InviteToken { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public bool IsBanned { get; set; }
    public DateTime? BannedAt { get; set; }
    public Guid? BannedById { get; set; }
    public string? BannedReason { get; set; }
    public string? TrackName { get; set; }
    public TeamRegistrationStatus RegistrationStatus { get; set; } = TeamRegistrationStatus.Pending;
    public DateTime RegisteredAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? ApprovedById { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class TeamMember
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public TeamMemberRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class Competition : ITenantEntity
{
    // CompetitionId == Id (tenant is itself)
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public GameModeType GameModeType { get; set; }
    public string ModeKey { get; set; } = string.Empty;
    public string ScoringProfileJson { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public CompetitionStatus Status { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public bool TracksEnabled { get; set; }
    public string TrackNamesJson { get; set; } = "[]";

    // CTF scoring defaults used when binding challenge templates into this competition.
    public int DefaultInitialPoints { get; set; } = 500;
    public int DefaultMinimumPoints { get; set; } = 100;
    public int DefaultDecayFactor { get; set; } = 450;
    public string DefaultDecayFunction { get; set; } = "sigmoid";
    public double DifficultyCoefficient { get; set; } = 1.0;
    public double FirstBloodBonusPercent { get; set; }
    public double SecondBloodBonusPercent { get; set; }
    public double ThirdBloodBonusPercent { get; set; }

    // AWD-specific configuration (nullable so CTF competitions are unaffected)
    public int? RoundDurationSeconds { get; set; }
    public int? TotalRounds { get; set; }
    public string? FlagFormat { get; set; } // e.g., "flag{{{0}}}"
    public string? FlagPath { get; set; }   // e.g., "/flag/flag.txt"

    // AWD scoring configuration
    public int? AttackPoints { get; set; } = 50;
    public int? ServiceOnlinePoints { get; set; } = 100;
    public int? ServiceDownPenalty { get; set; } = 50;
    public int? BeenAttackedPenalty { get; set; } = 50;
    public int? FlagValidityRounds { get; set; } = 2; // current + previous round

    // AWDP defense scoring
    public int? DefensePoints { get; set; } = 100;

    // AWDP-specific round scoring and attempt configuration.
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public bool? AwdpAllowAttackAfterBreakSuccess { get; set; }
    public bool? AwdpAllowDefenseAfterFixSuccess { get; set; }
    public bool? AwdpServicePenaltyEnabled { get; set; }
    public int? AwdpServicePenaltyPerRound { get; set; }
    public bool? AwdpViolationPenaltyEnabled { get; set; }
    public int? AwdpViolationPenalty { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }

    // KoH scoring configuration
    public int? ControlPointsPerInterval { get; set; } = 10;
    public int? PollIntervalSeconds { get; set; } = 30;
}

public class CompetitionCollaborator
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid UserId { get; set; }
    public CollaboratorRole Role { get; set; }
    public DateTime AddedAt { get; set; }
}

public class Challenge : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid? TemplateId { get; set; }
    public bool IsDeleting { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string TypeId { get; set; } = string.Empty;
    public PointsConfig PointsConfig { get; set; } = new();
    public double DifficultyCoefficient { get; set; } = 1.0;
    public bool EnableBloodBonus { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public string? AttachmentStorageKey { get; set; }
    public string? PatchTemplateStorageKey { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public string FlagPrefix { get; set; } = "flag";
    public string FlagEnvironmentVariable { get; set; } = "NOCTF_FLAG_UUID";
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string OrchestrationJson { get; set; } = "{}";
    public string? FlagSecret { get; set; }
    public CheckerConfig? CheckerConfig { get; set; }
    public KohAgentConfig? KohAgentConfig { get; set; }
    public string PenetrationConfigJson { get; set; } = "{}";
    public int? AwdpAttackScorePerRound { get; set; }
    public int? AwdpDefenseScorePerRound { get; set; }
    public int? AwdpMaxAttackAttempts { get; set; }
    public int? AwdpMaxDefenseAttempts { get; set; }
    public string? AwdpFixEntry { get; set; }
    public int? AwdpFixTimeoutSeconds { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChallengeTemplate
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = "ctf";
    public string? AttachmentUrl { get; set; }
    public string? PatchTemplateUrl { get; set; }
    public string? AttachmentStorageKey { get; set; }
    public string? PatchTemplateStorageKey { get; set; }
    public ChallengeDeploymentType DeploymentType { get; set; } = ChallengeDeploymentType.NoAttachment;
    public int? ExposedPort { get; set; }
    public string FlagEnvironmentVariable { get; set; } = "NOCTF_FLAG_UUID";
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string OrchestrationJson { get; set; } = "{}";
    public string? FlagSecret { get; set; }
    public CheckerConfig? CheckerConfig { get; set; }
    public KohAgentConfig? KohAgentConfig { get; set; }
    public string PenetrationConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class ChallengeHint : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Content { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CtfDynamicFlag : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string FlagUuid { get; set; } = string.Empty;
    public string EnvironmentVariable { get; set; } = "NOCTF_FLAG_UUID";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSubmittedAt { get; set; }
}

public class CompetitionLog : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Level { get; set; } = "info";
    public string EventType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid? TeamId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string MetadataJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}

public class CheatIncident : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid SuspectTeamId { get; set; }
    public Guid? VictimTeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public string SubmittedFlag { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool Resolved { get; set; }
    public DateTime? ResolvedAt { get; set; }
}

public enum ChallengeContainerMode
{
    SingleImage,
    DockerCompose
}

public enum ChallengeDeploymentType
{
    NoAttachment,
    StaticAttachment,
    DynamicContainer,
    StaticContainer
}

public class Submission : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? PenetrationFlagId { get; set; }
    public Guid UserId { get; set; }
    public string FlagContent { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

public enum PenetrationInstanceStatus
{
    None,
    Starting,
    Running,
    Stopping,
    Stopped,
    Resetting,
    Failed,
    Destroying,
    Destroyed,
    Expired
}

public enum PenetrationFlagInjectionType
{
    EnvironmentVariable,
    File
}

public class PenetrationTopologyTemplate
{
    public Guid Id { get; set; }
    public Guid ChallengeTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string NetworkConfigJson { get; set; } = "{}";
    public string EntryConfigJson { get; set; } = "{}";
    public string HealthcheckConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PenetrationNodeTemplate
{
    public Guid Id { get; set; }
    public Guid TopologyTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string? Command { get; set; }
    public string EntrypointJson { get; set; } = "[]";
    public string EnvironmentJson { get; set; } = "{}";
    public string PortsJson { get; set; } = "[]";
    public string VolumesJson { get; set; } = "[]";
    public string NetworksJson { get; set; } = "[]";
    public string DependsOnJson { get; set; } = "[]";
    public bool IsEntry { get; set; }
    public bool IsInternal { get; set; } = true;
    public string ResourceLimitJson { get; set; } = "{}";
    public string HealthcheckJson { get; set; } = "{}";
    public string OrchestrationJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PenetrationFlagTemplate
{
    public Guid Id { get; set; }
    public Guid TopologyTemplateId { get; set; }
    public Guid? NodeTemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stage { get; set; }
    public string? ValueSecret { get; set; }
    public string? ValueHash { get; set; }
    public int Score { get; set; }
    public bool IsDynamic { get; set; }
    public bool Visible { get; set; } = true;
    public PenetrationFlagInjectionType InjectionType { get; set; } = PenetrationFlagInjectionType.EnvironmentVariable;
    public string? InjectionKey { get; set; }
    public string? HintAfterSolved { get; set; }
    public int SolvedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PenetrationTopology : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string NetworkConfigJson { get; set; } = "{}";
    public string EntryConfigJson { get; set; } = "{}";
    public string HealthcheckConfigJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PenetrationNode : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TopologyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string? Command { get; set; }
    public string EntrypointJson { get; set; } = "[]";
    public string EnvironmentJson { get; set; } = "{}";
    public string PortsJson { get; set; } = "[]";
    public string VolumesJson { get; set; } = "[]";
    public string NetworksJson { get; set; } = "[]";
    public string DependsOnJson { get; set; } = "[]";
    public bool IsEntry { get; set; }
    public bool IsInternal { get; set; } = true;
    public string ResourceLimitJson { get; set; } = "{}";
    public string HealthcheckJson { get; set; } = "{}";
    public string OrchestrationJson { get; set; } = "{}";
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PenetrationFlag : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid TopologyId { get; set; }
    public Guid? NodeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Stage { get; set; }
    public string? ValueSecret { get; set; }
    public string? ValueHash { get; set; }
    public int Score { get; set; }
    public bool IsDynamic { get; set; }
    public bool Visible { get; set; } = true;
    public PenetrationFlagInjectionType InjectionType { get; set; } = PenetrationFlagInjectionType.EnvironmentVariable;
    public string? InjectionKey { get; set; }
    public string? HintAfterSolved { get; set; }
    public int SolvedCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TeamChallengeInstance : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid? TopologyId { get; set; }
    public PenetrationInstanceStatus Status { get; set; } = PenetrationInstanceStatus.None;
    public string? ComposeProjectName { get; set; }
    public Guid? RuntimeOperationId { get; set; }
    public string RenderedComposeYaml { get; set; } = string.Empty;
    public string ContainerIdsJson { get; set; } = "[]";
    public string PortMappingsJson { get; set; } = "{}";
    public string? EntryHost { get; set; }
    public int? EntryPort { get; set; }
    public string? EntryUrl { get; set; }
    public int ResetCount { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastActionAt { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DynamicFlagInstance : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public Guid FlagId { get; set; }
    public Guid InstanceId { get; set; }
    public string ValueSecret { get; set; } = string.Empty;
    public string ValueHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime GeneratedAt { get; set; }
    public DateTime? SolvedAt { get; set; }
    [NotMapped]
    public string? PlainValue { get; set; }
}

public class ScoreEvent : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ChallengeId { get; set; }
    public string ScoringKey { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public int PointsDelta { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
    public Guid? SourceSignalId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public string MetadataJson { get; set; } = "{}";

    /// <summary>AWD round number; null for CTF score events.</summary>
    public int? RoundNumber { get; set; }
}

public class ScoreSignal : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string SubjectType { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
    public string SignalType { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public int? RoundNumber { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Represents a single AWD round within a competition.</summary>
public class AwdRound : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public int RoundNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public AwdRoundStatus Status { get; set; }
}

/// <summary>Records a successful attack in an AWD round.</summary>
public class AwdAttackRecord : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid AttackerTeamId { get; set; }
    public Guid VictimTeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public int RoundNumber { get; set; }
    public string FlagContent { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

/// <summary>Stores a deterministically generated flag for a (team, challenge, round) tuple.</summary>
public class AwdFlag : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public int RoundNumber { get; set; }
    public string FlagContent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Tracks a running container instance for a (team, challenge) pair in AWD.</summary>
public class AwdGameBox : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string? ContainerInstanceId { get; set; }
    public string ProviderType { get; set; } = "docker";
    public string? PublicHost { get; set; }
    public string? EntryUrl { get; set; }
    public string? OrchestrationNamespace { get; set; }
    public string PortMappingsJson { get; set; } = "{}";
    public string RuntimeKind { get; set; } = "container";
    public string? ComposeProjectName { get; set; }
    public string? ComposeYaml { get; set; }
    public string? InternalHost { get; set; }
    public string InternalPortMappingsJson { get; set; } = "{}";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastInstanceActionAt { get; set; }
    public DateTime? LastFlagRefreshedAt { get; set; }
    public Guid? RuntimeOperationId { get; set; }
    public string? CleanupOwner { get; set; }
    public DateTime? CleanupLockedUntil { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Status of an AWD checker run.</summary>
public enum AwdCheckStatus
{
    Healthy,
    Down,
    Error
}

/// <summary>Records the result of a checker container run for a (team, challenge, round) tuple.</summary>
public class AwdCheckResult : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public int RoundNumber { get; set; }
    public AwdCheckStatus Status { get; set; }
    public string? Detail { get; set; }
    public DateTime CheckedAt { get; set; }
}

/// <summary>Configuration for a checker container attached to a challenge.</summary>
public class CheckerConfig
{
    public string? Image { get; set; }
    public string? Command { get; set; }
    public int? TimeoutSeconds { get; set; }

    /// <summary>Legacy optional exploit container image. AWDP now uses Image/Command as a single check container.</summary>
    public string? ExpImage { get; set; }

    /// <summary>Legacy optional exploit container command. AWDP now uses Image/Command as a single check container.</summary>
    public string? ExpCommand { get; set; }
}

/// <summary>Status of an AWDP patch submission.</summary>
public enum AwdpPatchStatus
{
    Pending,
    Applied,
    Verified,
    Rejected
}

public enum AwdpInstanceStatus
{
    InstanceNotCreated,
    InstanceCreating,
    InstanceRunning,
    InstanceExpired,
    InstanceResetting
}

public enum AwdpBreakStatus
{
    BreakNotStarted,
    BreakSubmitted,
    BreakSuccess,
    BreakFailed,
    AttackAttemptsExhausted
}

public enum AwdpFixStatus
{
    FixNotStarted,
    FixUploading,
    FixAuditing,
    FixRunning,
    FixChecking,
    FixSuccess,
    FixFailed,
    FixServiceError,
    FixScriptError,
    FixTimeout,
    AuditFailed,
    DefenseAttemptsExhausted,
    FixRuleViolation
}

public enum AwdpServiceStatus
{
    ServiceUnknown,
    ServiceOk,
    ServiceError
}

public enum AwdpRoundStatus
{
    RoundPending,
    RoundRunning,
    RoundScoring,
    RoundFinished
}

public class AwdpRound : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public int RoundNumber { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public AwdpRoundStatus Status { get; set; }
}

public class AwdpTeamChallengeState : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public AwdpInstanceStatus InstanceStatus { get; set; } = AwdpInstanceStatus.InstanceNotCreated;
    public AwdpBreakStatus BreakStatus { get; set; } = AwdpBreakStatus.BreakNotStarted;
    public AwdpFixStatus FixStatus { get; set; } = AwdpFixStatus.FixNotStarted;
    public AwdpServiceStatus ServiceStatus { get; set; } = AwdpServiceStatus.ServiceUnknown;
    public int AttackAttempts { get; set; }
    public int DefenseAttempts { get; set; }
    public DateTime? BreakSucceededAt { get; set; }
    public DateTime? FixSucceededAt { get; set; }
    public DateTime? LastBreakSubmittedAt { get; set; }
    public DateTime? DefenseRequestedAt { get; set; }
    public DateTime? LastFixSubmittedAt { get; set; }
    public string? LastValidationDetail { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class AwdpRoundScore : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public int RoundNumber { get; set; }
    public int AttackScoreDelta { get; set; }
    public int DefenseScoreDelta { get; set; }
    public int PenaltyDelta { get; set; }
    public int RoundScoreDelta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>Records a patch submission in an AWDP competition.</summary>
public class AwdpPatchSubmission : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string PatchArchiveUrl { get; set; } = string.Empty;
    public AwdpPatchStatus Status { get; set; }
    public AwdpFixStatus FixStatus { get; set; } = AwdpFixStatus.FixUploading;
    public int AttemptNumber { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FixEntry { get; set; } = "fix.sh";
    public DateTime SubmittedAt { get; set; }
    public DateTime? ValidatedAt { get; set; }
    public string? ValidationDetail { get; set; }
}

/// <summary>KoH agent configuration stored per challenge.</summary>
public class KohAgentConfig
{
    public int Port { get; set; } = 8080;
    public string? ApiKey { get; set; }
}

/// <summary>Tracks which team controls a KoH hill (challenge) during a time window.</summary>
public class KohControlRecord : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid TeamId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public class BackgroundTaskItem : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string Type { get; set; } = string.Empty;
    public BackgroundTaskStatus Status { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public int AttemptCount { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockOwner { get; set; }
}

/// <summary>
/// Durable, global outbox for deleting objects after their database references
/// have been removed. It is intentionally not tenant-filtered because template
/// objects do not belong to a competition.
/// </summary>
public class StorageCleanupItem
{
    public Guid Id { get; set; }
    public string StorageKey { get; set; } = string.Empty;
    public DateTime NotBefore { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockOwner { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Durable scheduling state for a plugin-owned competition engine.</summary>
public class CompetitionEngineState : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public string EngineKey { get; set; } = string.Empty;
    public DateTime? LastExecutedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class AwdpPlayerDefenseResult
{
    public const string DefenseSuccess = "defense_success";
    public const string ExpExploited = "defense_exp_exploited";
    public const string ServiceError = "defense_service_error";

    public static AwdpFixStatus ToVisibleFixStatus(AwdpFixStatus status)
        => status switch
        {
            AwdpFixStatus.FixSuccess => AwdpFixStatus.FixSuccess,
            AwdpFixStatus.FixFailed => AwdpFixStatus.FixFailed,
            AwdpFixStatus.FixServiceError
                or AwdpFixStatus.FixScriptError
                or AwdpFixStatus.FixTimeout
                or AwdpFixStatus.AuditFailed
                or AwdpFixStatus.FixRuleViolation => AwdpFixStatus.FixServiceError,
            _ => status
        };

    public static string? ToVisibleDetail(AwdpFixStatus status)
        => status switch
        {
            AwdpFixStatus.FixSuccess => DefenseSuccess,
            AwdpFixStatus.FixFailed => ExpExploited,
            AwdpFixStatus.FixServiceError
                or AwdpFixStatus.FixScriptError
                or AwdpFixStatus.FixTimeout
                or AwdpFixStatus.AuditFailed
                or AwdpFixStatus.FixRuleViolation => ServiceError,
            _ => null
        };
}

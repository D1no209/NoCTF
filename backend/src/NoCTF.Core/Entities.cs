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

public class User
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
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

    // CTF scoring defaults used when binding challenge templates into this competition.
    public int DefaultInitialPoints { get; set; } = 500;
    public int DefaultMinimumPoints { get; set; } = 100;
    public int DefaultDecayFactor { get; set; } = 450;
    public string DefaultDecayFunction { get; set; } = "quadratic";
    public double DifficultyCoefficient { get; set; } = 1.0;

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
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string DescriptionFormat { get; set; } = "markdown";
    public string TypeId { get; set; } = string.Empty;
    public PointsConfig PointsConfig { get; set; } = new();
    public double DifficultyCoefficient { get; set; } = 1.0;
    public string? AttachmentUrl { get; set; }
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string? FlagSecret { get; set; }
    public CheckerConfig? CheckerConfig { get; set; }
    public KohAgentConfig? KohAgentConfig { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ChallengeTemplate
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string TypeId { get; set; } = "ctf";
    public string? AttachmentUrl { get; set; }
    public string? ContainerImage { get; set; }
    public ChallengeContainerMode ContainerMode { get; set; } = ChallengeContainerMode.SingleImage;
    public string? ComposeYaml { get; set; }
    public string? ComposeProjectName { get; set; }
    public string? FlagSecret { get; set; }
    public CheckerConfig? CheckerConfig { get; set; }
    public KohAgentConfig? KohAgentConfig { get; set; }
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

public enum ChallengeContainerMode
{
    SingleImage,
    DockerCompose
}

public class Submission : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public Guid UserId { get; set; }
    public string FlagContent { get; set; } = string.Empty;
    public bool IsCorrect { get; set; }
    public DateTime SubmittedAt { get; set; }
    public string IpAddress { get; set; } = string.Empty;
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
    public DateTime? LastFlagRefreshedAt { get; set; }
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

    /// <summary>EXP (exploit) container image for AWDP patch validation.</summary>
    public string? ExpImage { get; set; }

    /// <summary>EXP (exploit) container command for AWDP patch validation.</summary>
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

/// <summary>Records a patch submission in an AWDP competition.</summary>
public class AwdpPatchSubmission : ITenantEntity
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid ChallengeId { get; set; }
    public string PatchArchiveUrl { get; set; } = string.Empty;
    public AwdpPatchStatus Status { get; set; }
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
}

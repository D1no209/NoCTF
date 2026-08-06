using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.Competitions;

/// <summary>Represents the common fields of a competition.</summary>
public sealed class Competition
{
    public Guid Id { get; set; }
    [MaxLength(160)]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid OwnerId { get; set; }
    public Guid[] ManagerIds { get; set; } = [];
    public Guid[] JudgeIds { get; set; } = [];
    public Guid[] ObserverIds { get; set; } = [];
    [ConcurrencyCheck]
    public int PermissionRevision { get; set; }
    public GameMode Mode { get; set; }
    public string ConfigurationJson { get; set; } = """{"schemaVersion":1}""";
    [ConcurrencyCheck]
    public int ConfigurationRevision { get; set; }
    public DateTimeOffset ConfigurationUpdatedAt { get; set; }
    public long LeaderboardRevision { get; set; }
    public CompetitionLeaderboardVisibility LeaderboardVisibility { get; set; }
    public DateTimeOffset? LeaderboardVisibilityStartsAt { get; set; }
    public DateTimeOffset? LeaderboardVisibilityAppliedAt { get; set; }
    [ConcurrencyCheck]
    public int LeaderboardVisibilityRevision { get; set; }
    public string? FrozenLeaderboardSnapshotJson { get; set; }
    [MaxLength(32)]
    public byte[] FlagDerivationSecret { get; set; } = [];
    public DateTimeOffset StartAt { get; set; }
    public DateTimeOffset EndAt { get; set; }
    public DateTimeOffset? RunningSince { get; set; }
    public long AccumulatedRunningSeconds { get; set; }
    [ConcurrencyCheck]
    public CompetitionStatus Status { get; set; }
    public bool TeamRegistrationAutoApprove { get; set; } = true;
    public int MaxTeamMembers { get; set; } = 5;
    public int MaxConcurrentRuntimeInstancesPerTeam { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public List<CompetitionLifecycleAudit> LifecycleAudits { get; set; } = [];
    public List<CompetitionLeaderboardVisibilityAudit> LeaderboardVisibilityAudits { get; set; } = [];
}

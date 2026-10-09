using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloCaptureState : short { Pending, Starting, Active, Stopping, Completed, Failed, RequiresReview }
public sealed class LiveSoloProgramCapture : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MediaSessionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public LiveSoloCaptureState State { get; set; }
    [MaxLength(256)] public string? EgressId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? RequestedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public DateTimeOffset? ImportedAt { get; set; }
    public DateTimeOffset? RawRemovedAt { get; set; }
}

/// <summary>Immutable state at a server observation time, never a projection of future current state.</summary>
public sealed class LiveSoloProgramFrame
{
    public Guid Id { get; set; }
    public Guid MediaSessionId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public LiveSoloMatchState MatchState { get; set; }
    public int RequiredWins { get; set; }
    public int LeftWins { get; set; }
    public int RightWins { get; set; }
    public Guid? LeftTeamId { get; set; }
    public Guid? RightTeamId { get; set; }
    [MaxLength(256)] public string? LeftTeamName { get; set; }
    [MaxLength(256)] public string? RightTeamName { get; set; }
    public Guid? RoundId { get; set; }
    public int? RoundNumber { get; set; }
    public LiveSoloRoundState? RoundState { get; set; }
    public long? TimelineRevision { get; set; }
    public long ActiveElapsedMilliseconds { get; set; }
    public int? LimitSeconds { get; set; }
    public bool Paused { get; set; }
    public List<LiveSoloProgramFrameQuestion> Questions { get; set; } = [];
}

public sealed class LiveSoloProgramFrameQuestion
{
    public Guid FrameId { get; set; }
    public int Position { get; set; }
    public Guid RoundQuestionId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    [MaxLength(256)] public string Title { get; set; } = string.Empty;
    public DateTimeOffset OpenedAt { get; set; }
}

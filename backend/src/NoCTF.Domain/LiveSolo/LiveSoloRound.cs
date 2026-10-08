using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloRoundState : short { Preparing, Countdown, Running, ConfirmingResult, Won, TimedOut, Canceled }
public enum LiveSoloQuestionReadiness : short { Waiting, Preparing, Ready, Failed }
public enum LiveSoloPauseSource : short { Match, Competition }

public sealed class LiveSoloRound : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int Number { get; set; }
    public int Replay { get; set; }
    public LiveSoloRoundState State { get; set; }
    public Guid QuestionGroupId { get; set; }
    public int LimitSeconds { get; set; } = 900;
    public int CountdownSeconds { get; set; } = 5;
    public long TimelineRevision { get; set; }
    public long LastAdmissionSequence { get; set; }
    public long LastResolvedSequence { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public Guid? WinningGameplayFactId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CountdownAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    [MaxLength(4000)] public string? AdjudicationReason { get; set; }
    public List<LiveSoloRoundQuestion> Questions { get; set; } = [];
    public List<LiveSoloPauseInterval> Pauses { get; set; } = [];
}

public sealed class LiveSoloRoundQuestion : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid RoundId { get; set; }
    public Guid CompetitionChallengeId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public int Position { get; set; }
    public int OpenOffsetSeconds { get; set; }
    public LiveSoloQuestionReadiness Readiness { get; set; }
    public DateTimeOffset? OpenedAt { get; set; }
    public List<LiveSoloRuntimeBinding> Runtimes { get; set; } = [];
}

public sealed class LiveSoloRuntimeBinding
{
    public Guid RoundQuestionId { get; set; }
    public LiveSoloSide Side { get; set; }
    public Guid RuntimeInstanceId { get; set; }
}

public sealed class LiveSoloPauseInterval
{
    public Guid Id { get; set; }
    public Guid RoundId { get; set; }
    public LiveSoloPauseSource Source { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>Associates the canonical GameplayFact with its immutable Round admission order.</summary>
public sealed class LiveSoloSubmission
{
    [Key] public Guid GameplayFactId { get; set; }
    public Guid RoundId { get; set; }
    public Guid RoundQuestionId { get; set; }
    public long AdmissionSequence { get; set; }
}

public sealed class LiveSoloDownloadEvidence
{
    [Key] public Guid GameplayFactId { get; set; }
    public Guid RoundQuestionId { get; set; }
}

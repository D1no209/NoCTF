using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloMatchState : short { AwaitingOpponents, Preparing, Countdown, Running, Paused, AwaitingAdjudication, Completed, Canceled }
public enum LiveSoloSide : short { Left, Right }
public enum LiveSoloSlotSource : short { Seed, Winner, Loser, Bye }

public sealed class LiveSoloMatch : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public LiveSoloBracketLane Lane { get; set; }
    public int Stage { get; set; }
    public int Position { get; set; }
    public int RequiredWins { get; set; } = 2;
    public LiveSoloMatchState State { get; set; }
    public bool Conditional { get; set; }
    public int LeftWins { get; set; }
    public int RightWins { get; set; }
    public Guid? WinnerTeamId { get; set; }
    public Guid? CurrentRoundId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    [MaxLength(4000)] public string? AdjudicationReason { get; set; }
    public List<LiveSoloMatchSlot> Slots { get; set; } = [];
    public List<LiveSoloRosterMember> Roster { get; set; } = [];
    public List<LiveSoloRound> Rounds { get; set; } = [];
}

public sealed class LiveSoloMatchSlot
{
    public Guid MatchId { get; set; }
    public LiveSoloSide Side { get; set; }
    public LiveSoloSlotSource Source { get; set; }
    public int? Seed { get; set; }
    public Guid? SourceMatchId { get; set; }
    public bool Resolved { get; set; }
    public Guid? TeamId { get; set; }
    public DateTimeOffset? RosterLockedAt { get; set; }
    public DateTimeOffset? ReadyConfirmedAt { get; set; }
}

public sealed class LiveSoloRosterMember
{
    public Guid MatchId { get; set; }
    public Guid TeamId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
}

public sealed class LiveSoloActiveTeamSlot
{
    public Guid CompetitionId { get; set; }
    public Guid TeamId { get; set; }
    public Guid MatchId { get; set; }
}

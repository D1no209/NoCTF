using System.ComponentModel.DataAnnotations;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloJudgeAction : short { Pause, Resume, VoidRound, ForfeitMatch }

/// <summary>Immutable staff decision; never substitutes for a FlagAttempt or a Round winner.</summary>
public sealed class LiveSoloAdjudication
{
    public Guid Id { get; set; }
    public Guid MatchId { get; set; }
    public Guid? RoundId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid? ForfeitingTeamId { get; set; }
    public LiveSoloJudgeAction Action { get; set; }
    [Required, MaxLength(4000)] public string Reason { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public LiveSoloMatchState PreviousMatchState { get; set; }
    public LiveSoloMatchState MatchState { get; set; }
    public LiveSoloRoundState? PreviousRoundState { get; set; }
    public LiveSoloRoundState? RoundState { get; set; }
    public int PreviousLeftWins { get; set; }
    public int PreviousRightWins { get; set; }
    public int LeftWins { get; set; }
    public int RightWins { get; set; }
    public long? PreviousTimelineRevision { get; set; }
    public long? TimelineRevision { get; set; }
}

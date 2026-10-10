using System.ComponentModel.DataAnnotations;
using NoCTF.Domain.Shared;

namespace NoCTF.Domain.LiveSolo;

public enum LiveSoloCorrectionState : short { Pending, Applied, Canceled }
public sealed class LiveSoloResultCorrection : IConcurrencyTracked
{
    public Guid Id { get; set; }
    public Guid CompetitionId { get; set; }
    public Guid MatchId { get; set; }
    public Guid ActorUserId { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
    public LiveSoloCorrectionState State { get; set; }
    public Guid PreviousWinnerTeamId { get; set; }
    public Guid WinnerTeamId { get; set; }
    public int PreviousLeftWins { get; set; }
    public int PreviousRightWins { get; set; }
    public int LeftWins { get; set; }
    public int RightWins { get; set; }
    [Required, MaxLength(4000)] public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    [MaxLength(4000)] public string? ResolutionReason { get; set; }
    public List<LiveSoloCorrectionMatch> Matches { get; set; } = [];
}
public sealed class LiveSoloCorrectionMatch
{
    public Guid CorrectionId { get; set; }
    public Guid MatchId { get; set; }
    public LiveSoloMatchState PreviousState { get; set; }
    public bool WasStarted { get; set; }
    public Guid? ReplacementMatchId { get; set; }
}

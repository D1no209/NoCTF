namespace NoCTF.Domain.Competitions.Events;

public static class CtfSolveBroadcastPolicy
{
    public static readonly CompetitionEventKind[] EventKinds =
    [
        CompetitionEventKind.GameplayFactReceived,
        CompetitionEventKind.GameplayFactAdjudicated,
        CompetitionEventKind.ScoringRecorded,
        CompetitionEventKind.FirstBloodAwarded,
        CompetitionEventKind.SecondBloodAwarded,
        CompetitionEventKind.ThirdBloodAwarded,
        CompetitionEventKind.FirstBloodCorrected,
        CompetitionEventKind.SecondBloodCorrected,
        CompetitionEventKind.ThirdBloodCorrected,
        CompetitionEventKind.FirstBloodWithdrawn,
        CompetitionEventKind.SecondBloodWithdrawn,
        CompetitionEventKind.ThirdBloodWithdrawn
    ];

    public static bool IsBlackout(GameMode mode, DateTimeOffset? frozenAt,
        DateTimeOffset? hiddenAt, DateTimeOffset now) =>
        mode == GameMode.Ctf && CompetitionLeaderboardVisibilityPolicy.EffectiveAt(
            frozenAt, hiddenAt, now) == CompetitionLeaderboardVisibility.Blackout;
}

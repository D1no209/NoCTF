using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Status;

public sealed record GameplayFactStatusView(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid? TeamId,
    Guid CompetitionChallengeId,
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt);

public interface IGameplayFactStatusReader
{
    Task<GameplayFactStatusView?> FindAsync(
        Guid competitionId,
        Guid gameplayFactId,
        Guid userId,
        CancellationToken cancellationToken);
}

public static class GameplayFactResultDisclosure
{
    public static GameplayFactResult? PlayerResult(
        GameplayFactResult? result,
        GameplayFactFailureCode? failureCode) =>
        IsProtected(failureCode) ? GameplayFactResult.Wrong : result;

    public static GameplayFactFailureCode? PlayerFailureCode(GameplayFactFailureCode? failureCode) =>
        IsProtected(failureCode) ? null : failureCode;

    private static bool IsProtected(GameplayFactFailureCode? failureCode) =>
        failureCode is GameplayFactFailureCode.ForeignTeamFlagDetected
            or GameplayFactFailureCode.AmbiguousFlagMatch;
}

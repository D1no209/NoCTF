using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.GameplayFacts.Status;

public sealed record AdminGameplayFactStatusView(
    Guid GameplayFactId,
    Guid CompetitionId,
    Guid? TeamId,
    Guid CompetitionChallengeId,
    Guid? ActorUserId,
    GameplayFactKind Kind,
    GameplayFactState State,
    GameplayFactResult? Result,
    GameplayFactFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    DateTimeOffset UpdatedAt);

public interface IAdminGameplayFactStatusReader
{
    Task<AdminGameplayFactStatusView?> FindAsync(
        Guid competitionId,
        Guid gameplayFactId,
        CancellationToken cancellationToken);
}

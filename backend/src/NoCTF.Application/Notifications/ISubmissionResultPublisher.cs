using NoCTF.Application.GameplayFacts.Status;

namespace NoCTF.Application.Notifications;

public interface IGameplayFactStatePublisher
{
    Task PublishAsync(Guid userId, GameplayFactStatusView result, CancellationToken cancellationToken);
}

public interface ILeaderboardPublisher
{
    Task PublishAsync(Guid competitionId, long projectionVersion, CancellationToken cancellationToken);
}

using NoCTF.Application.GameplayFacts.Status;

namespace NoCTF.Application.Notifications;

public interface IGameplayFactStatePublisher
{
    Task PublishAsync(Guid userId, GameplayFactStatusView result, CancellationToken cancellationToken);
}

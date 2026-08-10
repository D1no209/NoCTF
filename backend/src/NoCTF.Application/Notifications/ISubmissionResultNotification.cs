using NoCTF.Application.GameplayFacts.Status;

namespace NoCTF.Application.Notifications;

/// <summary>Non-sensitive completion message; submitted contents are never included.</summary>
public sealed record GameplayFactStateChangedNotification(Guid UserId, GameplayFactStatusView Result);

public interface IGameplayFactStateChangedNotification
{
    Task PublishAsync(GameplayFactStateChangedNotification notification, CancellationToken cancellationToken);
}

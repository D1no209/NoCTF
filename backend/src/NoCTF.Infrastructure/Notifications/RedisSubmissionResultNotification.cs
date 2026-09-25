using System.Text.Json;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Notifications;

public sealed class RedisGameplayFactStateChangedNotification(IConnectionMultiplexer? redis = null)
    : IGameplayFactStateChangedNotification
{
    public const string Channel = "noctf:gameplay-fact-states";

    public Task PublishAsync(GameplayFactStateChangedNotification notification, CancellationToken cancellationToken)
    {
        if (redis is null) return Task.CompletedTask;
        return redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(notification,
                NoCtfMessageJsonContext.Default.GameplayFactStateChangedNotification));
    }
}

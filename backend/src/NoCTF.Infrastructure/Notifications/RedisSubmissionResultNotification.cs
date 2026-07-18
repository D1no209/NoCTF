using System.Text.Json;
using NoCTF.Application.Notifications;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Notifications;

public sealed class RedisSubmissionResultNotification(IConnectionMultiplexer? redis = null)
    : ISubmissionResultNotification
{
    public const string Channel = "noctf:submission-results";

    public Task PublishAsync(SubmissionResultNotification notification, CancellationToken cancellationToken)
    {
        if (redis is null) return Task.CompletedTask;
        return redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(notification));
    }
}

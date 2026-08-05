using System.Text.Json;
using NoCTF.Application.Competitions.Events;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Competitions.Events;

public sealed class RedisCompetitionEventRefreshPublisher(
    IConnectionMultiplexer redis)
{
    public const string Channel = "noctf:competition-events:v1";

    public Task PublishAsync(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(message));
    }
}

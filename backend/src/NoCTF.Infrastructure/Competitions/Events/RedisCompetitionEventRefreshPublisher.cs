using System.Text.Json;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Competitions.Events;

public interface ICompetitionEventRefreshPublisher
{
    Task PublishAsync(CompetitionEventCommitted message, CancellationToken cancellationToken);
}

public sealed class RedisCompetitionEventRefreshPublisher(
    IConnectionMultiplexer redis) : ICompetitionEventRefreshPublisher
{
    public const string Channel = "noctf:competition-events:v1";

    public Task PublishAsync(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(message,
                NoCtfMessageJsonContext.Default.CompetitionEventCommitted));
    }
}

public sealed class NoOpCompetitionEventRefreshPublisher
    : ICompetitionEventRefreshPublisher
{
    public Task PublishAsync(
        CompetitionEventCommitted message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}

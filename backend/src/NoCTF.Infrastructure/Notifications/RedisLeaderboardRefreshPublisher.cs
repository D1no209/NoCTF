using System.Text.Json;
using NoCTF.Application.Notifications;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Notifications;

public sealed record LeaderboardRefreshNotification(Guid CompetitionId, DateTimeOffset GeneratedAt);

public sealed class RedisLeaderboardRefreshPublisher(IConnectionMultiplexer redis)
    : ILeaderboardRefreshPublisher
{
    public const string Channel = "noctf:leaderboard-refreshes";

    public Task PublishAsync(
        Guid competitionId,
        DateTimeOffset generatedAt,
        CancellationToken cancellationToken) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(new LeaderboardRefreshNotification(
                competitionId,
                generatedAt)));
}

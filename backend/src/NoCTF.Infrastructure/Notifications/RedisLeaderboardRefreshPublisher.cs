using System.Text.Json;
using NoCTF.Application.Notifications;
using NoCTF.Application.Scoring.Leaderboard;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Notifications;

public sealed class RedisLeaderboardRefreshPublisher(IConnectionMultiplexer redis)
    : ILeaderboardRefreshPublisher
{
    public const string Channel = "noctf:leaderboard-refreshes";

    public Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken) =>
        redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(Channel),
            JsonSerializer.Serialize(new ScoreboardUpdated(
                projection.Snapshot.CompetitionId,
                projection.Snapshot.Version,
                projection.Schema.Revision,
                projection.ChallengeCatalog.Revision)));
}

using System.Diagnostics;
using System.Text.Json;
using NoCTF.Application.Notifications;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Application.Scoring.Leaderboard;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Notifications;

public sealed class RedisLeaderboardRefreshPublisher(IConnectionMultiplexer redis)
    : ILeaderboardRefreshPublisher
{
    public const string Channel = "noctf:leaderboard-refreshes";

    public async Task PublishAsync(
        ScoreboardProjection projection,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(Channel),
                JsonSerializer.Serialize(ScoreboardUpdated.From(projection),
                    NoCtfMessageJsonContext.Default.ScoreboardUpdated));
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordRedisOperation(
                "leaderboard_publish",
                outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }
}

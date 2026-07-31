using Microsoft.Extensions.Configuration;
using NoCTF.Application.Scoring.Leaderboard;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed class RedisLeaderboardSubscriptionRegistry(
    IConnectionMultiplexer redis,
    IConfiguration configuration) : ILeaderboardSubscriptionRegistry
{
    private const int DefaultSubscriberTtlSeconds = 90;
    private const string TouchScript =
        """
        local current_time = redis.call('TIME')
        local now_milliseconds = (current_time[1] * 1000) + math.floor(current_time[2] / 1000)
        local ttl_milliseconds = tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now_milliseconds)
        local became_active = redis.call('ZCARD', KEYS[1]) == 0
        redis.call('ZADD', KEYS[1], now_milliseconds + ttl_milliseconds, ARGV[1])
        redis.call('PEXPIRE', KEYS[1], ttl_milliseconds * 2)
        if became_active then
            return 1
        end
        return 0
        """;
    private const string HasActiveScript =
        """
        local current_time = redis.call('TIME')
        local now_milliseconds = (current_time[1] * 1000) + math.floor(current_time[2] / 1000)
        redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now_milliseconds)
        local count = redis.call('ZCARD', KEYS[1])
        if count == 0 then
            redis.call('DEL', KEYS[1])
            return 0
        end
        return 1
        """;
    private const string RemoveScript =
        """
        redis.call('ZREM', KEYS[1], ARGV[1])
        if redis.call('ZCARD', KEYS[1]) == 0 then
            redis.call('DEL', KEYS[1])
        end
        return 0
        """;

    private readonly long ttlMilliseconds = checked((long)TimeSpan.FromSeconds(
        Math.Max(
            1,
            configuration.GetValue(
                "Leaderboard:SubscriberTtlSeconds",
                DefaultSubscriberTtlSeconds))).TotalMilliseconds);

    public async Task<bool> TouchAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        cancellationToken.ThrowIfCancellationRequested();
        var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(
            TouchScript,
            [SubscriptionKey(competitionId)],
            [subscriberId, ttlMilliseconds]);
        return result == 1;
    }

    public async Task<bool> HasActiveAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = (long)await redis.GetDatabase().ScriptEvaluateAsync(
            HasActiveScript,
            [SubscriptionKey(competitionId)]);
        return result == 1;
    }

    public async Task RemoveAsync(
        Guid competitionId,
        string subscriberId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriberId);
        cancellationToken.ThrowIfCancellationRequested();
        await redis.GetDatabase().ScriptEvaluateAsync(
            RemoveScript,
            [SubscriptionKey(competitionId)],
            [subscriberId]);
    }

    private static RedisKey SubscriptionKey(Guid competitionId) =>
        new($"leaderboard:{{{competitionId:N}}}:subscribers");
}

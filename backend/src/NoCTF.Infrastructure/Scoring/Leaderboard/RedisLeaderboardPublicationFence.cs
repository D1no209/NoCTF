using System.Diagnostics;
using System.Globalization;
using NoCTF.Application.Observability;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Scoring.Leaderboard;

public sealed record LeaderboardFencedPayload(long Fence, string Payload);

public interface ILeaderboardPublicationFence
{
    Task<long> IssueAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
    Task<bool> TryCommitAsync(
        Guid competitionId,
        long fence,
        string payload,
        CancellationToken cancellationToken);
    Task<LeaderboardFencedPayload?> GetAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<bool> IsCurrentAsync(Guid competitionId, long fence, CancellationToken cancellationToken);
    Task InvalidateAsync(Guid competitionId, long minimumFence, CancellationToken cancellationToken);
}

/// <summary>
/// Keeps the authoritative leaderboard payload and its publication fence in one Redis
/// transaction. The fence counter is separate so losing all Redis data simply causes the
/// next projection to seed a new version from its UTC timestamp.
/// </summary>
public sealed class RedisLeaderboardPublicationFence(IConnectionMultiplexer redis)
    : ILeaderboardPublicationFence
{
    private const string IssueScript = """
        local current = redis.call('GET', KEYS[1])
        local floor = ARGV[1]
        local function less_than(left, right)
            if string.len(left) ~= string.len(right) then
                return string.len(left) < string.len(right)
            end
            return left < right
        end
        if (not current) or less_than(current, floor) then
            redis.call('SET', KEYS[1], floor)
        else
            redis.call('INCR', KEYS[1])
        end
        return redis.call('GET', KEYS[1])
        """;

    private const string CommitScript = """
        local current = redis.call('HGET', KEYS[1], 'fence')
        local incoming = ARGV[1]
        local function less_than(left, right)
            if string.len(left) ~= string.len(right) then
                return string.len(left) < string.len(right)
            end
            return left < right
        end
        if current and not less_than(current, incoming) then
            return 0
        end
        redis.call('HSET', KEYS[1], 'fence', incoming, 'payload', ARGV[2])
        return 1
        """;

    private const string InvalidateScript = """
        local current = redis.call('GET', KEYS[1])
        local floor = ARGV[1]
        local function less_than(left, right)
            if string.len(left) ~= string.len(right) then
                return string.len(left) < string.len(right)
            end
            return left < right
        end
        local next
        if (not current) or less_than(current, floor) then
            next = floor
            redis.call('SET', KEYS[1], next)
        else
            next = redis.call('INCR', KEYS[1])
        end
        redis.call('HSET', KEYS[2], 'fence', next)
        redis.call('HDEL', KEYS[2], 'payload')
        return next
        """;

    public async Task<long> IssueAsync(
        Guid competitionId,
        long minimumFence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(
                IssueScript,
                [FenceKey(competitionId)],
                [minimumFence.ToString(CultureInfo.InvariantCulture)]);
            return long.Parse(result.ToString(), CultureInfo.InvariantCulture);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordRedisOperation(
                "leaderboard_fence_issue",
                outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    public async Task<bool> TryCommitAsync(
        Guid competitionId,
        long fence,
        string payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            var result = await redis.GetDatabase().ScriptEvaluateAsync(
                CommitScript,
                [PayloadKey(competitionId)],
                [fence.ToString(CultureInfo.InvariantCulture), payload]);
            return (long)result == 1;
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordRedisOperation(
                "leaderboard_fence_commit",
                outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    public async Task<LeaderboardFencedPayload?> GetAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var values = await redis.GetDatabase().HashGetAsync(
            PayloadKey(competitionId),
            ["fence", "payload"]);
        if (values[0].IsNullOrEmpty || values[1].IsNullOrEmpty)
            return null;
        return new(
            long.Parse(values[0].ToString(), CultureInfo.InvariantCulture),
            values[1].ToString());
    }

    public async Task<bool> IsCurrentAsync(
        Guid competitionId,
        long fence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = await redis.GetDatabase().HashGetAsync(
            PayloadKey(competitionId),
            "fence");
        return !current.IsNullOrEmpty
            && long.TryParse(current.ToString(), CultureInfo.InvariantCulture, out var value)
            && value == fence;
    }

    public async Task InvalidateAsync(
        Guid competitionId,
        long minimumFence,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var started = Stopwatch.GetTimestamp();
        var outcome = "success";
        try
        {
            _ = await redis.GetDatabase().ScriptEvaluateAsync(
                InvalidateScript,
                [FenceKey(competitionId), PayloadKey(competitionId)],
                [minimumFence.ToString(CultureInfo.InvariantCulture)]);
        }
        catch
        {
            outcome = "failure";
            throw;
        }
        finally
        {
            NoCtfTelemetry.RecordRedisOperation(
                "leaderboard_fence_invalidate",
                outcome,
                Stopwatch.GetElapsedTime(started).TotalSeconds);
        }
    }

    internal static RedisKey FenceKey(Guid competitionId) =>
        $"leaderboard:{competitionId:N}:fence";

    internal static RedisKey PayloadKey(Guid competitionId) =>
        $"leaderboard:{competitionId:N}:published";
}

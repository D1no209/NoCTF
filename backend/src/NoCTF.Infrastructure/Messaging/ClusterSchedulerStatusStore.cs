using System.Text.Json;
using StackExchange.Redis;

namespace NoCTF.Infrastructure.Messaging;

public interface IClusterSchedulerStatusStore
{
    Task TakeOverAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken);

    Task RenewAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken);

    Task ReleaseAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken);

    Task<ClusterSchedulerStatus?> ReadAsync(CancellationToken cancellationToken);

    Task ReportLeaderAsync(
        ClusterLeaderStatus status,
        CancellationToken cancellationToken);

    Task<ClusterLeaderStatus?> ReadLeaderAsync(CancellationToken cancellationToken);
}

public sealed record ClusterSchedulerStatus(
    string OwnerNode,
    DateTimeOffset TakenOverAt);

public sealed record ClusterLeaderStatus(
    string LeaderNode,
    DateTimeOffset ObservedAt);

public sealed class RedisClusterSchedulerStatusStore(
    IConnectionMultiplexer redis) : IClusterSchedulerStatusStore
{
    private const string StatusKey = "noctf:cluster-scheduler:status";
    private const string LeaderKey = "noctf:cluster-leader:status";
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(15);

    public async Task TakeOverAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        var value = Serialize(status);
        _ = await redis.GetDatabase()
            .StringSetAsync(StatusKey, value, Lease)
            .WaitAsync(cancellationToken);
    }

    public async Task RenewAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        var renewed = await redis.GetDatabase().ScriptEvaluateAsync(
                """
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('PEXPIRE', KEYS[1], ARGV[2])
                end
                return 0
                """,
                [StatusKey],
                [Serialize(status), (long)Lease.TotalMilliseconds])
            .WaitAsync(cancellationToken);
        if ((long)renewed == 0)
        {
            throw new InvalidOperationException(
                "The cluster scheduler diagnostic lease is no longer owned by this node.");
        }
    }

    public async Task ReleaseAsync(
        ClusterSchedulerStatus status,
        CancellationToken cancellationToken)
    {
        _ = await redis.GetDatabase().ScriptEvaluateAsync(
                """
                if redis.call('GET', KEYS[1]) == ARGV[1] then
                    return redis.call('DEL', KEYS[1])
                end
                return 0
                """,
                [StatusKey],
                [Serialize(status)])
            .WaitAsync(cancellationToken);
    }

    public async Task<ClusterSchedulerStatus?> ReadAsync(
        CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase()
            .StringGetAsync(StatusKey)
            .WaitAsync(cancellationToken);
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ClusterSchedulerStatus>((string)value!);
    }

    public async Task ReportLeaderAsync(
        ClusterLeaderStatus status,
        CancellationToken cancellationToken)
    {
        _ = await redis.GetDatabase()
            .StringSetAsync(LeaderKey, JsonSerializer.Serialize(status), Lease)
            .WaitAsync(cancellationToken);
    }

    public async Task<ClusterLeaderStatus?> ReadLeaderAsync(
        CancellationToken cancellationToken)
    {
        var value = await redis.GetDatabase()
            .StringGetAsync(LeaderKey)
            .WaitAsync(cancellationToken);
        return value.IsNullOrEmpty
            ? null
            : JsonSerializer.Deserialize<ClusterLeaderStatus>((string)value!);
    }

    private static string Serialize(ClusterSchedulerStatus status) =>
        JsonSerializer.Serialize(status);
}

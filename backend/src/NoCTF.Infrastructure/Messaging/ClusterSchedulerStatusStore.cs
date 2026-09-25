using System.Text.Json;
using System.Text.Json.Serialization;
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
}

public sealed record ClusterSchedulerStatus(
    string OwnerNode,
    DateTimeOffset TakenOverAt);

public sealed class RedisClusterSchedulerStatusStore(
    IConnectionMultiplexer redis) : IClusterSchedulerStatusStore
{
    private const string StatusKey = "noctf:cluster-scheduler:status";
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
            : JsonSerializer.Deserialize(
                (string)value!, ClusterSchedulerStatusJsonContext.Default.ClusterSchedulerStatus);
    }

    private static string Serialize(ClusterSchedulerStatus status) =>
        JsonSerializer.Serialize(status,
            ClusterSchedulerStatusJsonContext.Default.ClusterSchedulerStatus);
}

[JsonSerializable(typeof(ClusterSchedulerStatus))]
internal partial class ClusterSchedulerStatusJsonContext : JsonSerializerContext;

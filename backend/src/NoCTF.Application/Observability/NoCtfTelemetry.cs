using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NoCTF.Application.Messaging;

namespace NoCTF.Application.Observability;

public static class NoCtfTelemetry
{
    public const string MeterName = "NoCTF";
    public const string ActivitySourceName = "NoCTF";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> ApiRequests = Meter.CreateCounter<long>(
        "noctf.api.requests", unit: "{request}");
    private static readonly Histogram<double> ApiDuration = Meter.CreateHistogram<double>(
        "noctf.api.request.duration", unit: "s");
    private static readonly Counter<long> RateLimitRejections = Meter.CreateCounter<long>(
        "noctf.api.rate_limit.rejections", unit: "{request}");
    private static readonly UpDownCounter<long> SignalRConnections = Meter.CreateUpDownCounter<long>(
        "noctf.signalr.connections", unit: "{connection}");
    private static readonly Histogram<double> SignalRPublishDuration = Meter.CreateHistogram<double>(
        "noctf.signalr.publish.duration", unit: "s");
    private static readonly Counter<long> SignalRPublishes = Meter.CreateCounter<long>(
        "noctf.signalr.publishes", unit: "{message}");
    private static readonly Histogram<double> RedisOperationDuration = Meter.CreateHistogram<double>(
        "noctf.redis.operation.duration", unit: "s");
    private static readonly Counter<long> RedisFailures = Meter.CreateCounter<long>(
        "noctf.redis.failures", unit: "{operation}");
    private static readonly Histogram<double> RunnerClaimDuration = Meter.CreateHistogram<double>(
        "noctf.runner.claim.duration", unit: "s");
    private static readonly Counter<long> RunnerClaimAttempts = Meter.CreateCounter<long>(
        "noctf.runner.claim.attempts", unit: "{attempt}");
    private static readonly Counter<long> RuntimeOperations = Meter.CreateCounter<long>(
        "noctf.runtime.operations", unit: "{operation}");
    private static readonly Histogram<double> LeaderboardProjectionDuration = Meter.CreateHistogram<double>(
        "noctf.leaderboard.projection.duration", unit: "s");
    private static readonly Histogram<long> LeaderboardProjectionFacts = Meter.CreateHistogram<long>(
        "noctf.leaderboard.projection.facts", unit: "{fact}");
    private static readonly Histogram<long> LeaderboardProjectionTeams = Meter.CreateHistogram<long>(
        "noctf.leaderboard.projection.teams", unit: "{team}");
    private static readonly Counter<long> LeaderboardPublishFailures = Meter.CreateCounter<long>(
        "noctf.leaderboard.publish.failures", unit: "{failure}");
    private static long _dirtyCompetitionCount;
    private static long _oldestDirtyAgeSeconds;
    private static long _waitingRuntimeCount;
    private static long _oldestWaitingRuntimeAgeSeconds;
    private static readonly ConcurrentDictionary<string, WorkerQueueSnapshot> WorkerQueueSnapshots =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, RunnerCapacitySnapshot> RunnerCapacitySnapshots =
        new(StringComparer.Ordinal);

    static NoCtfTelemetry()
    {
        Meter.CreateObservableGauge(
            "noctf.leaderboard.dirty.competitions",
            () => Interlocked.Read(ref _dirtyCompetitionCount),
            unit: "{competition}");
        Meter.CreateObservableGauge(
            "noctf.leaderboard.dirty.oldest_age",
            () => Interlocked.Read(ref _oldestDirtyAgeSeconds),
            unit: "s");
        Meter.CreateObservableGauge(
            "noctf.runtime.waiting",
            () => Interlocked.Read(ref _waitingRuntimeCount),
            unit: "{runtime}");
        Meter.CreateObservableGauge(
            "noctf.runtime.waiting.oldest_age",
            () => Interlocked.Read(ref _oldestWaitingRuntimeAgeSeconds),
            unit: "s");
        Meter.CreateObservableGauge(
            "noctf.worker.queue.depth",
            ObserveWorkerQueueDepth,
            unit: "{message}");
        Meter.CreateObservableGauge(
            "noctf.worker.queue.oldest_age",
            ObserveWorkerQueueOldestAge,
            unit: "s");
        Meter.CreateObservableGauge(
            "noctf.runner.online",
            ObserveRunnerOnline,
            unit: "{runner}");
        Meter.CreateObservableGauge(
            "noctf.runner.capacity.available",
            ObserveRunnerCapacityAvailable);
        Meter.CreateObservableGauge(
            "noctf.runner.capacity.total",
            ObserveRunnerCapacityTotal);
    }

    public static void RecordApiRequest(string endpoint, string outcome, double elapsedSeconds)
    {
        var tags = new TagList { { "endpoint", endpoint }, { "outcome", outcome } };
        ApiRequests.Add(1, tags);
        ApiDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordRateLimitRejection(string endpoint) =>
        RateLimitRejections.Add(1, new TagList { { "endpoint", endpoint } });

    public static void SignalRConnected(string endpoint) =>
        SignalRConnections.Add(1, new TagList { { "endpoint", endpoint } });

    public static void SignalRDisconnected(string endpoint) =>
        SignalRConnections.Add(-1, new TagList { { "endpoint", endpoint } });

    public static void RecordSignalRPublish(string endpoint, string outcome, double elapsedSeconds)
    {
        var tags = new TagList { { "endpoint", endpoint }, { "outcome", outcome } };
        SignalRPublishes.Add(1, tags);
        SignalRPublishDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordRedisOperation(string endpoint, string outcome, double elapsedSeconds)
    {
        var tags = new TagList { { "endpoint", endpoint }, { "outcome", outcome } };
        RedisOperationDuration.Record(elapsedSeconds, tags);
        if (!string.Equals(outcome, "success", StringComparison.Ordinal))
            RedisFailures.Add(1, tags);
    }

    public static void RecordRunnerClaim(string pool, string outcome, int attempts, double elapsedSeconds)
    {
        var tags = new TagList { { "pool", pool }, { "outcome", outcome } };
        RunnerClaimAttempts.Add(attempts, tags);
        RunnerClaimDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordRuntimeOperation(string endpoint, string outcome) =>
        RuntimeOperations.Add(1, new TagList { { "endpoint", endpoint }, { "outcome", outcome } });

    public static void RecordLeaderboardProjection(
        string mode,
        string outcome,
        double elapsedSeconds,
        int factCount,
        int teamCount)
    {
        var tags = new TagList { { "mode", mode }, { "outcome", outcome } };
        LeaderboardProjectionDuration.Record(elapsedSeconds, tags);
        LeaderboardProjectionFacts.Record(factCount, tags);
        LeaderboardProjectionTeams.Record(teamCount, tags);
    }

    public static void RecordLeaderboardPublishFailure(string endpoint) =>
        LeaderboardPublishFailures.Add(1, new TagList { { "endpoint", endpoint } });

    public static void UpdateOperationalSnapshot(
        long dirtyCompetitionCount,
        TimeSpan oldestDirtyAge,
        long waitingRuntimeCount,
        TimeSpan oldestWaitingRuntimeAge)
    {
        Interlocked.Exchange(ref _dirtyCompetitionCount, Math.Max(0, dirtyCompetitionCount));
        Interlocked.Exchange(
            ref _oldestDirtyAgeSeconds,
            Math.Max(0, (long)oldestDirtyAge.TotalSeconds));
        Interlocked.Exchange(ref _waitingRuntimeCount, Math.Max(0, waitingRuntimeCount));
        Interlocked.Exchange(
            ref _oldestWaitingRuntimeAgeSeconds,
            Math.Max(0, (long)oldestWaitingRuntimeAge.TotalSeconds));
    }

    public static void UpdateWorkerQueueSnapshot(
        string queue,
        long depth,
        TimeSpan oldestAge)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        if (!WorkerQueueMonitoringNames.IsKnown(queue))
            throw new ArgumentOutOfRangeException(nameof(queue), queue, "Unknown worker queue.");
        WorkerQueueSnapshots[queue] = new(
            Math.Max(0, depth),
            Math.Max(0, (long)oldestAge.TotalSeconds));
    }

    public static void UpdateRunnerCapacitySnapshot(
        string pool,
        string runnerId,
        bool online,
        long availableMemoryBytes,
        long totalMemoryBytes,
        long availableNanoCpus,
        long totalNanoCpus,
        long availablePids,
        long totalPids)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pool);
        ArgumentException.ThrowIfNullOrWhiteSpace(runnerId);
        RunnerCapacitySnapshots[$"{pool}\u001f{runnerId}"] = new(
            pool,
            online,
            ClampAvailable(online, availableMemoryBytes, totalMemoryBytes),
            Math.Max(0, totalMemoryBytes),
            ClampAvailable(online, availableNanoCpus, totalNanoCpus),
            Math.Max(0, totalNanoCpus),
            ClampAvailable(online, availablePids, totalPids),
            Math.Max(0, totalPids));
    }

    private static IEnumerable<Measurement<long>> ObserveWorkerQueueDepth() =>
        WorkerQueueMonitoringNames.All.Select(queue => new Measurement<long>(
            WorkerQueueSnapshots.TryGetValue(queue, out var snapshot) ? snapshot.Depth : 0,
            new KeyValuePair<string, object?>("queue", queue)));

    private static IEnumerable<Measurement<long>> ObserveWorkerQueueOldestAge() =>
        WorkerQueueMonitoringNames.All.Select(queue => new Measurement<long>(
            WorkerQueueSnapshots.TryGetValue(queue, out var snapshot)
                ? snapshot.OldestAgeSeconds
                : 0,
            new KeyValuePair<string, object?>("queue", queue)));

    private static IEnumerable<Measurement<long>> ObserveRunnerOnline() =>
        RunnerCapacitySnapshots.Values
            .GroupBy(snapshot => snapshot.Pool, StringComparer.Ordinal)
            .Select(group => new Measurement<long>(
                group.LongCount(snapshot => snapshot.Online),
                new KeyValuePair<string, object?>("pool", group.Key)));

    private static IEnumerable<Measurement<long>> ObserveRunnerCapacityAvailable() =>
        ObserveRunnerCapacity(total: false);

    private static IEnumerable<Measurement<long>> ObserveRunnerCapacityTotal() =>
        ObserveRunnerCapacity(total: true);

    private static IEnumerable<Measurement<long>> ObserveRunnerCapacity(bool total) =>
        RunnerCapacitySnapshots.Values
            .GroupBy(snapshot => snapshot.Pool, StringComparer.Ordinal)
            .SelectMany(group => new[]
            {
                RunnerCapacityMeasurement(
                    group.Key,
                    "memory",
                    group.Where(snapshot => snapshot.Online).Sum(snapshot =>
                        total ? snapshot.TotalMemoryBytes : snapshot.AvailableMemoryBytes)),
                RunnerCapacityMeasurement(
                    group.Key,
                    "cpu",
                    group.Where(snapshot => snapshot.Online).Sum(snapshot =>
                        total ? snapshot.TotalNanoCpus : snapshot.AvailableNanoCpus)),
                RunnerCapacityMeasurement(
                    group.Key,
                    "pids",
                    group.Where(snapshot => snapshot.Online).Sum(snapshot =>
                        total ? snapshot.TotalPids : snapshot.AvailablePids))
            });

    private static Measurement<long> RunnerCapacityMeasurement(
        string pool,
        string resource,
        long value) => new(
        value,
        new KeyValuePair<string, object?>("pool", pool),
        new KeyValuePair<string, object?>("resource", resource));

    private static long ClampAvailable(bool online, long available, long total) =>
        online ? Math.Clamp(available, 0, Math.Max(0, total)) : 0;

    private sealed record RunnerCapacitySnapshot(
        string Pool,
        bool Online,
        long AvailableMemoryBytes,
        long TotalMemoryBytes,
        long AvailableNanoCpus,
        long TotalNanoCpus,
        long AvailablePids,
        long TotalPids);

    private sealed record WorkerQueueSnapshot(long Depth, long OldestAgeSeconds);
}

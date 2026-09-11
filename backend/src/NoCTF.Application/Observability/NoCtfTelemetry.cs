using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

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
    private static readonly Counter<long> HumanVerifications = Meter.CreateCounter<long>(
        "noctf.api.human_verification", unit: "{verification}");
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
    private static readonly Counter<long> LeaderboardInvalidations = Meter.CreateCounter<long>(
        "noctf.leaderboard.cache.invalidations", unit: "{invalidation}");
    private static readonly Counter<long> LeaderboardMergeEvents = Meter.CreateCounter<long>(
        "noctf.leaderboard.merge.events", unit: "{event}");
    private static readonly Counter<long> LeaderboardMergeDispatches = Meter.CreateCounter<long>(
        "noctf.leaderboard.merge.dispatches", unit: "{projection}");
    private static readonly Counter<long> LeaderboardCacheMissRebuilds = Meter.CreateCounter<long>(
        "noctf.leaderboard.cache_miss.rebuilds", unit: "{rebuild}");
    private static readonly Counter<long> SchedulerTakeovers = Meter.CreateCounter<long>(
        "noctf.scheduler.takeovers", unit: "{takeover}");
    private static readonly Histogram<double> SchedulerRebuildDuration = Meter.CreateHistogram<double>(
        "noctf.scheduler.rebuild.duration", unit: "s");
    private static readonly Histogram<long> SchedulerEntries = Meter.CreateHistogram<long>(
        "noctf.scheduler.rebuild.entries", unit: "{entry}");
    private static readonly Histogram<double> SchedulerDispatchLateness = Meter.CreateHistogram<double>(
        "noctf.scheduler.dispatch.lateness", unit: "s");
    private static readonly Counter<long> SchedulerDispatches = Meter.CreateCounter<long>(
        "noctf.scheduler.dispatches", unit: "{message}");
    private static readonly Counter<long> SchedulerSkippedTicks = Meter.CreateCounter<long>(
        "noctf.scheduler.skipped_ticks", unit: "{tick}");
    private static readonly Counter<long> AccountNotificationIssuances = Meter.CreateCounter<long>(
        "noctf.account_notification.issuances", unit: "{issuance}");
    private static readonly Counter<long> AccountNotificationDeliveries = Meter.CreateCounter<long>(
        "noctf.account_notification.deliveries", unit: "{delivery}");
    private static long _waitingRuntimeCount;
    private static long _oldestWaitingRuntimeAgeSeconds;
    private static readonly ConcurrentDictionary<string, RunnerCapacitySnapshot> RunnerCapacitySnapshots =
        new(StringComparer.Ordinal);

    static NoCtfTelemetry()
    {
        Meter.CreateObservableGauge(
            "noctf.runtime.waiting",
            () => Interlocked.Read(ref _waitingRuntimeCount),
            unit: "{runtime}");
        Meter.CreateObservableGauge(
            "noctf.runtime.waiting.oldest_age",
            () => Interlocked.Read(ref _oldestWaitingRuntimeAgeSeconds),
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

    public static void RecordApiRequest(string endpoint, string outcome, double elapsedSeconds, ApiRequestKind kind = ApiRequestKind.Rest)
    {
        var tags = new TagList { { "endpoint", endpoint }, { "outcome", outcome }, { "request_kind", kind.ToString().ToLowerInvariant() } };
        ApiRequests.Add(1, tags);
        ApiDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordRateLimitRejection(string endpoint) =>
        RateLimitRejections.Add(1, new TagList { { "endpoint", endpoint } });

    public static void RecordHumanVerification(string provider, string action, string outcome) =>
        HumanVerifications.Add(1, new TagList
        {
            { "provider", provider.ToLowerInvariant() },
            { "action", action.ToLowerInvariant() },
            { "outcome", outcome.ToLowerInvariant() }
        });

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

    public static void RecordLeaderboardInvalidation(string outcome) =>
        LeaderboardInvalidations.Add(1, new TagList { { "outcome", outcome } });

    public static void RecordLeaderboardMergeEvent(bool startedWindow) =>
        LeaderboardMergeEvents.Add(1, new TagList
        {
            { "outcome", startedWindow ? "window_started" : "merged" }
        });

    public static void RecordLeaderboardMergeDispatch(string outcome) =>
        LeaderboardMergeDispatches.Add(1, new TagList { { "outcome", outcome } });

    public static void RecordLeaderboardCacheMissRebuild(string outcome) =>
        LeaderboardCacheMissRebuilds.Add(1, new TagList { { "outcome", outcome } });

    public static void RecordSchedulerTakeover() => SchedulerTakeovers.Add(1);

    public static void RecordSchedulerRebuild(
        string outcome,
        double elapsedSeconds,
        int entryCount)
    {
        var tags = new TagList { { "outcome", outcome } };
        SchedulerRebuildDuration.Record(elapsedSeconds, tags);
        SchedulerEntries.Record(Math.Max(0, entryCount), tags);
    }

    public static void RecordSchedulerDispatch(
        string kind,
        string outcome,
        double latenessSeconds)
    {
        var tags = new TagList { { "kind", kind }, { "outcome", outcome } };
        SchedulerDispatches.Add(1, tags);
        SchedulerDispatchLateness.Record(Math.Max(0, latenessSeconds), tags);
    }

    public static void RecordSchedulerSkippedTicks(string kind, long count)
    {
        if (count <= 0)
            return;
        SchedulerSkippedTicks.Add(count, new TagList { { "kind", kind } });
    }

    public static void RecordAccountNotificationIssuance(string kind, string outcome) =>
        AccountNotificationIssuances.Add(1, new TagList
        {
            { "kind", kind },
            { "outcome", outcome }
        });

    public static void RecordAccountNotificationDelivery(string kind, string outcome) =>
        AccountNotificationDeliveries.Add(1, new TagList
        {
            { "kind", kind },
            { "outcome", outcome }
        });

    public static void UpdateOperationalSnapshot(
        long waitingRuntimeCount,
        TimeSpan oldestWaitingRuntimeAge)
    {
        Interlocked.Exchange(ref _waitingRuntimeCount, Math.Max(0, waitingRuntimeCount));
        Interlocked.Exchange(
            ref _oldestWaitingRuntimeAgeSeconds,
            Math.Max(0, (long)oldestWaitingRuntimeAge.TotalSeconds));
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
}

public enum ApiRequestKind { Rest, SignalR, Upload, Download }

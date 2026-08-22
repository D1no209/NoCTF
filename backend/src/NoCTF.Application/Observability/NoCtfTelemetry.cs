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
    private static readonly Counter<long> WorkerMessages = Meter.CreateCounter<long>(
        "noctf.worker.messages", unit: "{message}");
    private static readonly Histogram<double> WorkerHandlerDuration = Meter.CreateHistogram<double>(
        "noctf.worker.handler.duration", unit: "s");
    private static readonly Histogram<double> WorkerQueueWait = Meter.CreateHistogram<double>(
        "noctf.worker.queue.wait", unit: "s");

    private static long _dirtyCompetitionCount;
    private static long _oldestDirtyAgeSeconds;
    private static long _waitingRuntimeCount;
    private static long _oldestWaitingRuntimeAgeSeconds;

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

    public static void RecordWorkerMessage(
        string queue,
        string outcome,
        double handlerSeconds,
        double waitSeconds)
    {
        var tags = new TagList { { "queue", queue }, { "outcome", outcome } };
        WorkerMessages.Add(1, tags);
        WorkerHandlerDuration.Record(handlerSeconds, tags);
        WorkerQueueWait.Record(Math.Max(0, waitSeconds), tags);
    }

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
}

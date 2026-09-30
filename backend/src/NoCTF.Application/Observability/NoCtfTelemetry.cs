using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using NoCTF.Application.Admission;
using NoCTF.Domain.Gameplay;

namespace NoCTF.Application.Observability;

public enum GameplayFactPerformanceStage
{
    ChallengeAttemptStateRead,
    AdmissionLoad,
    AdmissionRecheck,
    PersistenceCommit,
    MessagePublish,
    DispatchAge,
    WorkerEvaluation,
    WorkerCompletion
}

public enum RuntimeDispatchPerformanceStage
{
    TargetRead,
    DefinitionPreparation,
    CapacityClaim,
    Persistence,
    TransactionCommit,
    PostCommitPublish
}

public enum RunnerCapacityTransactionOperation
{
    Claim,
    Release
}

public enum WebhookRecoveryScanKind
{
    Outbox,
    Retry
}

public enum WebhookRecoveryScanOutcome
{
    Empty,
    Work,
    Failed
}

public enum CapSiteverifyFailureReason
{
    UpstreamError,
    RateLimited,
    Timeout,
    Transport,
    CircuitOpen,
    InvalidResponse
}

public enum RuntimeOperationMetricKind
{
    Flag,
    FixRequest,
    FixUpload,
    PlayerCreate,
    PlayerExtend,
    PlayerStop,
    SharedCreate,
    SharedStop,
    TeamCreate,
    TeamExtend,
    TeamStop,
    TestCreate,
    TestExtend,
    TestStop,
    Terminate,
    ForceTerminate
}

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
    private static readonly Histogram<double> CapSiteverifyDuration =
        Meter.CreateHistogram<double>("noctf.cap.siteverify.duration", unit: "s");
    private static readonly Counter<long> CapSiteverifyFailures =
        Meter.CreateCounter<long>("noctf.cap.siteverify.failures", unit: "{failure}");
    private static readonly Counter<long> CapTelemetryPolls =
        Meter.CreateCounter<long>("noctf.cap.telemetry.polls", unit: "{poll}");
    private static readonly UpDownCounter<long> SignalRConnections = Meter.CreateUpDownCounter<long>(
        "noctf.signalr.connections", unit: "{connection}");
    private static readonly Histogram<double> SignalRPublishDuration = Meter.CreateHistogram<double>(
        "noctf.signalr.publish.duration", unit: "s");
    private static readonly Counter<long> SignalRPublishes = Meter.CreateCounter<long>(
        "noctf.signalr.publishes", unit: "{message}");
    private static readonly Histogram<double> NatsOperationDuration = Meter.CreateHistogram<double>(
        "noctf.nats.operation.duration", unit: "s");
    private static readonly Counter<long> NatsFailures = Meter.CreateCounter<long>(
        "noctf.nats.failures", unit: "{operation}");
    private static readonly Counter<long> PlatformLogLiveDrops = Meter.CreateCounter<long>(
        "noctf.platform_log.live_drops", unit: "{log}");
    private static readonly Histogram<double> RunnerClaimDuration = Meter.CreateHistogram<double>(
        "noctf.runner.claim.duration", unit: "s");
    private static readonly Counter<long> RunnerClaimAttempts = Meter.CreateCounter<long>(
        "noctf.runner.claim.attempts", unit: "{attempt}");
    private static readonly Counter<long> RunnerCapacityTransactionRetries =
        Meter.CreateCounter<long>(
            "noctf.runner.capacity.transaction_retries", unit: "{retry}");
    private static readonly Counter<long> RunnerCapacityTransactionExhaustions =
        Meter.CreateCounter<long>(
            "noctf.runner.capacity.transaction_exhaustions", unit: "{exhaustion}");
    private static readonly Counter<long> RuntimeOperations = Meter.CreateCounter<long>(
        "noctf.runtime.operations", unit: "{operation}");
    private static readonly Counter<long> RuntimeMutationFailures = Meter.CreateCounter<long>(
        "noctf.runtime.mutation.failures", unit: "{failure}");
    private static readonly Histogram<double> RuntimeStopDuration = Meter.CreateHistogram<double>(
        "noctf.runtime.stop.duration", unit: "s");
    private static readonly Histogram<double> RuntimeDispatchStageDuration = Meter.CreateHistogram<double>(
        "noctf.runtime.dispatch.stage.duration", unit: "s");
    private static readonly Histogram<double> RuntimeStopQueueDelay = Meter.CreateHistogram<double>(
        "noctf.runtime.stop.queue_delay", unit: "s");
    private static readonly Counter<long> RuntimeStopForces = Meter.CreateCounter<long>(
        "noctf.runtime.stop.force_total", unit: "{operation}");
    private static readonly Counter<long> RuntimeStopResourcesRemaining = Meter.CreateCounter<long>(
        "noctf.runtime.stop.resources_remaining_total", unit: "{operation}");
    private static readonly Counter<long> GameplayFactSubmissions = Meter.CreateCounter<long>(
        "noctf.gameplay_fact.submissions", unit: "{submission}");
    private static readonly Counter<long> GameplayFactProcessing = Meter.CreateCounter<long>(
        "noctf.gameplay_fact.processing", unit: "{submission}");
    private static readonly Histogram<double> GameplayFactProcessingDuration =
        Meter.CreateHistogram<double>(
            "noctf.gameplay_fact.processing.duration",
            unit: "s");
    private static readonly Histogram<double> GameplayFactStageDuration =
        Meter.CreateHistogram<double>("noctf.gameplay_fact.stage.duration", unit: "s");
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
    private static readonly Counter<long> FusionCacheReads = Meter.CreateCounter<long>(
        "noctf.fusion_cache.reads", unit: "{read}");
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
    private static readonly Histogram<double> WebhookQueueAge = Meter.CreateHistogram<double>(
        "noctf.webhook.queue.age", unit: "s");
    private static readonly Histogram<double> WebhookProjectionWait = Meter.CreateHistogram<double>(
        "noctf.webhook.projection.wait", unit: "s");
    private static readonly Histogram<double> WebhookHttpAttemptDuration = Meter.CreateHistogram<double>(
        "noctf.webhook.http.attempt.duration", unit: "s");
    private static readonly Counter<long> WebhookRetries = Meter.CreateCounter<long>(
        "noctf.webhook.retries", unit: "{retry}");
    private static readonly Counter<long> WebhookRecoveryScans = Meter.CreateCounter<long>(
        "noctf.webhook.recovery.scans", unit: "{scan}");
    private static readonly Histogram<double> WebhookRecoveryScanDuration =
        Meter.CreateHistogram<double>("noctf.webhook.recovery.scan.duration", unit: "s");
    private static readonly Counter<long> WebhookDeadLetters = Meter.CreateCounter<long>(
        "noctf.webhook.dead_letters", unit: "{delivery}");
    private static readonly Counter<long> WebhookMaterializationRaces =
        Meter.CreateCounter<long>(
            "noctf.webhook.materialization_races", unit: "{race}");
    private static readonly ConcurrentDictionary<string, RunnerCapacitySnapshot> RunnerCapacitySnapshots =
        new(StringComparer.Ordinal);
    private static long runtimeWaitingCount;
    private static long runtimeWaitingOldestAgeSeconds;
    private static long webhookPendingCount;
    private static long webhookOldestPendingAgeSeconds;
    private static long webhookConsumerLag;
    private static long capActive;
    private static long capTelemetryAvailable;
    private static long capVerifiedToday;
    private static long capFailedToday;
    private static long capRateLimitedToday;
    private static long capObservedAtUnixSeconds;
    private static double capAverageSolveDurationSeconds;

    static NoCtfTelemetry()
    {
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
        Meter.CreateObservableGauge("noctf.runtime.waiting",
            () => Volatile.Read(ref runtimeWaitingCount), unit: "{runtime}");
        Meter.CreateObservableGauge("noctf.runtime.waiting.oldest_age",
            () => Volatile.Read(ref runtimeWaitingOldestAgeSeconds), unit: "s");
        Meter.CreateObservableGauge("noctf.webhook.pending",
            () => Volatile.Read(ref webhookPendingCount), unit: "{delivery}");
        Meter.CreateObservableGauge("noctf.webhook.oldest_pending.age",
            () => Volatile.Read(ref webhookOldestPendingAgeSeconds), unit: "s");
        Meter.CreateObservableGauge("noctf.webhook.consumer.lag",
            () => Volatile.Read(ref webhookConsumerLag), unit: "{event}");
        Meter.CreateObservableGauge("noctf.cap.active",
            () => Volatile.Read(ref capActive));
        Meter.CreateObservableGauge("noctf.cap.telemetry.available",
            () => Volatile.Read(ref capTelemetryAvailable));
        Meter.CreateObservableGauge("noctf.cap.verified.today",
            () => Volatile.Read(ref capVerifiedToday), unit: "{challenge}");
        Meter.CreateObservableGauge("noctf.cap.failed.today",
            () => Volatile.Read(ref capFailedToday), unit: "{challenge}");
        Meter.CreateObservableGauge("noctf.cap.rate_limited.today",
            () => Volatile.Read(ref capRateLimitedToday), unit: "{request}");
        Meter.CreateObservableGauge("noctf.cap.average_solve.duration",
            () => Volatile.Read(ref capAverageSolveDurationSeconds), unit: "s");
        Meter.CreateObservableGauge("noctf.cap.telemetry.observed_at",
            () => Volatile.Read(ref capObservedAtUnixSeconds), unit: "s");
    }

    public static void SetRuntimeWaitingSnapshot(long count, long oldestAgeSeconds)
    {
        Volatile.Write(ref runtimeWaitingCount, Math.Max(0, count));
        Volatile.Write(ref runtimeWaitingOldestAgeSeconds, Math.Max(0, oldestAgeSeconds));
    }

    public static void SetWebhookPendingSnapshot(
        long count, long oldestAgeSeconds, long consumerLag)
    {
        Volatile.Write(ref webhookPendingCount, Math.Max(0, count));
        Volatile.Write(ref webhookOldestPendingAgeSeconds, Math.Max(0, oldestAgeSeconds));
        Volatile.Write(ref webhookConsumerLag, Math.Max(0, consumerLag));
    }

    public static void RecordWebhookQueueAge(double seconds) =>
        WebhookQueueAge.Record(Math.Max(0, seconds));

    public static void RecordWebhookProjectionWait(double seconds) =>
        WebhookProjectionWait.Record(Math.Max(0, seconds));

    public static void RecordWebhookHttpAttempt(double seconds, string outcome) =>
        WebhookHttpAttemptDuration.Record(Math.Max(0, seconds),
            new TagList { { "outcome", outcome } });

    public static void RecordWebhookRetry(string stage) =>
        WebhookRetries.Add(1, new TagList { { "stage", stage } });

    public static void RecordWebhookRecoveryScan(
        WebhookRecoveryScanKind kind,
        WebhookRecoveryScanOutcome outcome,
        double elapsedSeconds)
    {
        var tags = new TagList
        {
            { "kind", kind.ToString().ToLowerInvariant() },
            { "outcome", outcome.ToString().ToLowerInvariant() }
        };
        WebhookRecoveryScans.Add(1, tags);
        WebhookRecoveryScanDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordWebhookDeadLetter(string reason) =>
        WebhookDeadLetters.Add(1, new TagList { { "reason", reason } });

    public static void RecordWebhookMaterializationRace() =>
        WebhookMaterializationRaces.Add(1);

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

    public static void RecordCapSiteverify(
        HumanVerificationAction action,
        HumanVerificationResult result,
        double elapsedSeconds) =>
        CapSiteverifyDuration.Record(elapsedSeconds, new TagList
        {
            { "action", action.ToString().ToLowerInvariant() },
            { "outcome", result.ToString().ToLowerInvariant() }
        });

    public static void RecordCapSiteverifyFailure(
        HumanVerificationAction action,
        CapSiteverifyFailureReason reason) =>
        CapSiteverifyFailures.Add(1, new TagList
        {
            { "action", action.ToString().ToLowerInvariant() },
            { "reason", reason.ToString().ToLowerInvariant() }
        });

    public static void SetCapTelemetryActive(bool active) =>
        Volatile.Write(ref capActive, active ? 1 : 0);

    public static void RecordCapTelemetryPoll(CapTelemetryReadOutcome outcome) =>
        CapTelemetryPolls.Add(1, new TagList
        {
            { "outcome", outcome.ToString().ToLowerInvariant() }
        });

    public static void SetCapTelemetryUnavailable() =>
        Volatile.Write(ref capTelemetryAvailable, 0);

    public static void ClearCapTelemetrySnapshot()
    {
        Volatile.Write(ref capTelemetryAvailable, 0);
        Volatile.Write(ref capVerifiedToday, 0);
        Volatile.Write(ref capFailedToday, 0);
        Volatile.Write(ref capRateLimitedToday, 0);
        Volatile.Write(ref capAverageSolveDurationSeconds, 0);
        Volatile.Write(ref capObservedAtUnixSeconds, 0);
    }

    public static void SetCapTelemetrySnapshot(
        CapDailyTelemetry snapshot,
        DateTimeOffset observedAt)
    {
        Volatile.Write(ref capVerifiedToday, snapshot.Verified);
        Volatile.Write(ref capFailedToday, snapshot.Failed);
        Volatile.Write(ref capRateLimitedToday, snapshot.RateLimited);
        Volatile.Write(ref capAverageSolveDurationSeconds,
            snapshot.AverageSolveDurationSeconds);
        Volatile.Write(ref capObservedAtUnixSeconds, observedAt.ToUnixTimeSeconds());
        Volatile.Write(ref capTelemetryAvailable, 1);
    }

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

    public static void RecordRunnerClaim(string pool, string outcome, int attempts, double elapsedSeconds)
    {
        var tags = new TagList { { "pool", pool }, { "outcome", outcome } };
        RunnerClaimAttempts.Add(attempts, tags);
        RunnerClaimDuration.Record(elapsedSeconds, tags);
    }

    public static void RecordRunnerCapacityTransactionRetry(
        RunnerCapacityTransactionOperation operation) =>
        RunnerCapacityTransactionRetries.Add(1,
            new TagList { { "operation", MetricName(operation) } });

    public static void RecordRunnerCapacityTransactionExhaustion(
        RunnerCapacityTransactionOperation operation) =>
        RunnerCapacityTransactionExhaustions.Add(1,
            new TagList { { "operation", MetricName(operation) } });

    public static void RecordRuntimeOperation(
        RuntimeOperationMetricKind operation,
        string outcome) =>
        RuntimeOperations.Add(1, new TagList
        {
            { "operation", MetricName(operation) },
            { "outcome", outcome }
        });

    public static void RecordRuntimeMutationFailure<TFailure>(
        RuntimeOperationMetricKind operation,
        TFailure failure)
        where TFailure : struct, Enum =>
        RuntimeMutationFailures.Add(1, new TagList
        {
            { "operation", MetricName(operation) },
            { "failure", MetricName(failure) }
        });

    public static void RecordRuntimeStopDuration(
        string provider,
        string kind,
        string stage,
        string outcome,
        double elapsedSeconds) =>
        RuntimeStopDuration.Record(Math.Max(0, elapsedSeconds), new TagList
        {
            { "provider", provider.ToLowerInvariant() },
            { "kind", kind.ToLowerInvariant() },
            { "stage", stage },
            { "outcome", outcome }
        });

    public static void RecordRuntimeStopQueueDelay(
        string provider,
        string kind,
        double elapsedSeconds) =>
        RuntimeStopQueueDelay.Record(Math.Max(0, elapsedSeconds), new TagList
        {
            { "provider", provider.ToLowerInvariant() },
            { "kind", kind.ToLowerInvariant() }
        });

    public static void RecordRuntimeStopForce(string provider, string reason) =>
        RuntimeStopForces.Add(1, new TagList
        {
            { "provider", provider.ToLowerInvariant() },
            { "reason", reason }
        });

    public static void RecordRuntimeStopResourcesRemaining(string provider, string kind) =>
        RuntimeStopResourcesRemaining.Add(1, new TagList
        {
            { "provider", provider.ToLowerInvariant() },
            { "kind", kind.ToLowerInvariant() }
        });

    public static void RecordGameplayFactSubmissions(
        GameplayFactKind kind,
        long count = 1)
    {
        if (count <= 0)
            return;
        var submissionKind = SubmissionKind(kind);
        if (submissionKind is not null)
        {
            GameplayFactSubmissions.Add(
                count,
                new TagList { { "kind", submissionKind } });
        }
    }

    public static void RecordNatsOperation(string operation, string outcome, double elapsedSeconds)
    {
        var tags = new TagList { { "operation", operation }, { "outcome", outcome } };
        NatsOperationDuration.Record(elapsedSeconds, tags);
        if (!string.Equals(outcome, "success", StringComparison.Ordinal))
            NatsFailures.Add(1, tags);
    }

    public static void RecordPlatformLogLiveDrop() => PlatformLogLiveDrops.Add(1);

    public static void RecordGameplayFactProcessing(
        GameplayFactKind kind,
        GameplayFactState state,
        GameplayFactResult? result,
        double elapsedSeconds)
    {
        var submissionKind = kind is GameplayFactKind.FlagAttempt
            or GameplayFactKind.BreakAttempt
            ? SubmissionKind(kind)
            : null;
        var outcome = state switch
        {
            GameplayFactState.PlatformFailed => "platform_error",
            GameplayFactState.Completed when result == GameplayFactResult.Correct => "correct",
            GameplayFactState.Completed => "incorrect",
            _ => null
        };
        if (submissionKind is null || outcome is null)
            return;

        var tags = new TagList
        {
            { "kind", submissionKind },
            { "outcome", outcome }
        };
        GameplayFactProcessing.Add(1, tags);
        GameplayFactProcessingDuration.Record(Math.Max(0, elapsedSeconds), tags);
    }

    public static void RecordGameplayFactStage(
        GameplayFactPerformanceStage stage,
        double elapsedSeconds) =>
        GameplayFactStageDuration.Record(
            Math.Max(0, elapsedSeconds),
            new TagList { { "stage", stage.ToString() } });

    public static void RecordRuntimeDispatchStage(
        RuntimeDispatchPerformanceStage stage,
        double elapsedSeconds) =>
        RuntimeDispatchStageDuration.Record(
            Math.Max(0, elapsedSeconds),
            new TagList { { "stage", stage.ToString() } });

    private static string? SubmissionKind(GameplayFactKind kind) => kind switch
    {
        GameplayFactKind.FlagAttempt => "flag",
        GameplayFactKind.BreakAttempt => "break",
        GameplayFactKind.FixAttempt => "fix",
        _ => null
    };

    private static string MetricName<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

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

    public static void RecordFusionCacheRead(string cache, string tier, bool hit) =>
        FusionCacheReads.Add(1, new TagList
        {
            { "cache", cache },
            { "tier", tier },
            { "result", hit ? "hit" : "miss" }
        });

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

    public static void UpdateRunnerCapacitySnapshot(
        string pool,
        string runnerId,
        bool online,
        long availableMemoryBytes,
        long totalMemoryBytes,
        long availableCpuMillicores,
        long totalCpuMillicores,
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
            ClampAvailable(online, availableCpuMillicores, totalCpuMillicores),
            Math.Max(0, totalCpuMillicores),
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
                        total ? snapshot.TotalCpuMillicores : snapshot.AvailableCpuMillicores)),
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
        long AvailableCpuMillicores,
        long TotalCpuMillicores,
        long AvailablePids,
        long TotalPids);
}

public enum ApiRequestKind { Rest, SignalR, Upload, Download }

using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using Wolverine;
using Wolverine.Runtime.Agents;

namespace NoCTF.Infrastructure.Messaging;

public sealed class MaintenanceTickAgent(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ClusterSchedulingState state,
    ClusterSchedulerNodeIdentity nodeIdentity,
    IClusterSchedulerStatusStore statusStore,
    ILogger<MaintenanceTickAgent> logger)
    : SingularAgent(AgentName)
{
    public const string AgentName = "noctf-maintenance-ticks";
    private static readonly TimeSpan RebuildInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan LoopInterval = TimeSpan.FromMilliseconds(200);
    private static readonly HashSet<ClusterScheduleKind> RebuiltKinds =
    [
        ClusterScheduleKind.AwdRound,
        ClusterScheduleKind.KohPoll
    ];

    private readonly Dictionary<string, ClusterScheduleEntry> entries =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateTimeOffset> lastDispatchedDue =
        new(StringComparer.Ordinal);
    private PriorityQueue<ClusterScheduleEntry, long> queue = new();
    private CancellationTokenSource? stopping;
    private Task? loop;
    private DateTimeOffset nextRebuildAt;
    private DateTimeOffset nextStatusRenewalAt;
    private ClusterSchedulerStatus? activeStatus;

    protected override async Task startAsync(CancellationToken cancellationToken)
    {
        stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        state.Activating(nodeIdentity.Value);
        var startedAt = timeProvider.GetUtcNow();
        try
        {
            AddFixedSchedules(startedAt);
            var elapsed = await RebuildAsync(startedAt, initial: true, stopping.Token);
            activeStatus = new(nodeIdentity.Value, startedAt);
            await statusStore.TakeOverAsync(activeStatus, stopping.Token);
            state.Activated(
                nodeIdentity.Value,
                startedAt,
                timeProvider.GetUtcNow(),
                elapsed,
                entries.Count);
            nextRebuildAt = timeProvider.GetUtcNow().Add(RebuildInterval);
            nextStatusRenewalAt = timeProvider.GetUtcNow().Add(RebuildInterval);
            loop = RunAsync(stopping.Token);
        }
        catch (Exception exception)
        {
            state.Failed(exception.GetType().Name, TimeSpan.Zero);
            throw;
        }
    }

    protected override async Task stopAsync(CancellationToken cancellationToken)
    {
        if (stopping is null || loop is null)
        {
            state.Stopped();
            return;
        }
        await stopping.CancelAsync();
        try
        {
            await loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (activeStatus is not null)
            {
                try
                {
                    await statusStore.ReleaseAsync(activeStatus, cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Failed to release the cluster scheduler diagnostic lease.");
                }
            }
            state.Stopped();
            stopping.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(LoopInterval, timeProvider);
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = timeProvider.GetUtcNow();
            if (activeStatus is not null && now >= nextStatusRenewalAt)
            {
                try
                {
                    await statusStore.RenewAsync(activeStatus, cancellationToken);
                }
                catch (Exception exception)
                {
                    state.Failed(exception.GetType().Name, TimeSpan.Zero);
                    logger.LogError(
                        exception,
                        "Cluster scheduler diagnostic lease renewal failed.");
                }
                nextStatusRenewalAt = timeProvider.GetUtcNow().Add(RebuildInterval);
            }
            if (now >= nextRebuildAt)
            {
                try
                {
                    var elapsed = await RebuildAsync(now, initial: false, cancellationToken);
                    state.Rebuilt(timeProvider.GetUtcNow(), elapsed, entries.Count);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Cluster schedule rebuild failed.");
                }
                nextRebuildAt = timeProvider.GetUtcNow().Add(RebuildInterval);
            }

            await DrainDueAsync(now, cancellationToken);
            await timer.WaitForNextTickAsync(cancellationToken);
        }
    }

    private async Task<TimeSpan> RebuildAsync(
        DateTimeOffset now,
        bool initial,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var source = scope.ServiceProvider.GetRequiredService<IClusterScheduleSource>();
            var rebuilt = await source.RebuildAsync(now, cancellationToken);
            MergeRebuiltSchedules(rebuilt, now, initial);
            return Stopwatch.GetElapsedTime(started);
        }
        catch (Exception exception)
        {
            var elapsed = Stopwatch.GetElapsedTime(started);
            state.Failed(exception.GetType().Name, elapsed);
            throw;
        }
    }

    private void MergeRebuiltSchedules(
        IReadOnlyList<ClusterScheduleEntry> rebuilt,
        DateTimeOffset now,
        bool initial)
    {
        var rebuiltKeys = rebuilt.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var key in entries
                     .Where(pair => RebuiltKinds.Contains(pair.Value.Kind)
                         && !rebuiltKeys.Contains(pair.Key))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            entries.Remove(key);
        }

        foreach (var candidate in rebuilt)
        {
            var wasKnown = entries.ContainsKey(candidate.Key)
                || lastDispatchedDue.ContainsKey(candidate.Key);
            if (lastDispatchedDue.TryGetValue(candidate.Key, out var lastDue)
                && candidate.DueAt <= lastDue)
            {
                if (!entries.ContainsKey(candidate.Key) && candidate.Interval is { } interval)
                    entries[candidate.Key] = candidate.At(
                        ClusterScheduleClock.NextAfterDispatch(now, interval));
                continue;
            }

            entries[candidate.Key] = candidate;
            if ((initial || !wasKnown) && candidate.SkippedTicks > 0)
            {
                NoCtfTelemetry.RecordSchedulerSkippedTicks(
                    ScheduleKind(candidate.Kind),
                    candidate.SkippedTicks);
            }
        }

        RebuildPriorityQueue();
    }

    private async Task DrainDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        while (queue.TryPeek(out var entry, out var priority)
            && priority <= now.UtcTicks)
        {
            queue.Dequeue();
            if (!entries.TryGetValue(entry.Key, out var current)
                || current.DueAt != entry.DueAt)
                continue;
            entries.Remove(entry.Key);

            try
            {
                await PublishAsync(entry.Message, cancellationToken);
                lastDispatchedDue[entry.Key] = entry.DueAt;
                NoCtfTelemetry.RecordSchedulerDispatch(
                    ScheduleKind(entry.Kind),
                    "success",
                    (now - entry.DueAt).TotalSeconds);
                if (entry.Interval is { } interval)
                {
                    var next = entry.At(ClusterScheduleClock.NextAfterDispatch(now, interval));
                    entries[next.Key] = next;
                    queue.Enqueue(next, next.DueAt.UtcTicks);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                NoCtfTelemetry.RecordSchedulerDispatch(
                    ScheduleKind(entry.Kind),
                    "failure",
                    (now - entry.DueAt).TotalSeconds);
                state.Failed(exception.GetType().Name, TimeSpan.Zero);
                logger.LogError(
                    exception,
                    "Cluster scheduler failed to publish {ScheduleKind}.",
                    entry.Kind);
                var retry = entry.At(now.AddSeconds(1));
                entries[retry.Key] = retry;
                queue.Enqueue(retry, retry.DueAt.UtcTicks);
                break;
            }
        }
    }

    private async Task PublishAsync(object message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = scopeFactory.CreateAsyncScope();
        var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        await (message switch
        {
            AdvanceAwdRound value => bus.PublishAsync(value),
            DispatchAwdCheckers value => bus.PublishAsync(value),
            PollKohChallenge value => bus.PublishAsync(value),
            AdvanceCompetitionLifecycle value => bus.PublishAsync(value),
            RefreshDirtyLeaderboards value => bus.PublishAsync(value),
            _ => throw new ArgumentOutOfRangeException(
                nameof(message),
                message.GetType().FullName,
                "Unsupported cluster schedule message type.")
        });
    }

    private void AddFixedSchedules(DateTimeOffset now)
    {
        AddFixed(new(
            "awd-checkers",
            ClusterScheduleKind.AwdChecker,
            now,
            TimeSpan.FromSeconds(1),
            new DispatchAwdCheckers(now)));
        AddFixed(new(
            "competition-lifecycle",
            ClusterScheduleKind.CompetitionLifecycle,
            now,
            TimeSpan.FromSeconds(30),
            new AdvanceCompetitionLifecycle(now)));
        // Removed in phase 9 when leaderboard invalidation becomes event driven.
        AddFixed(new(
            "leaderboard-refresh",
            ClusterScheduleKind.LeaderboardRefresh,
            now,
            TimeSpan.FromSeconds(15),
            new RefreshDirtyLeaderboards(now)));
    }

    private void AddFixed(ClusterScheduleEntry entry)
    {
        entries[entry.Key] = entry;
        queue.Enqueue(entry, entry.DueAt.UtcTicks);
    }

    private void RebuildPriorityQueue()
    {
        queue = new PriorityQueue<ClusterScheduleEntry, long>();
        foreach (var entry in entries.Values)
            queue.Enqueue(entry, entry.DueAt.UtcTicks);
    }

    private static string ScheduleKind(ClusterScheduleKind kind) => kind switch
    {
        ClusterScheduleKind.AwdRound => "awd_round",
        ClusterScheduleKind.AwdChecker => "awd_checker",
        ClusterScheduleKind.KohPoll => "koh_poll",
        ClusterScheduleKind.CompetitionLifecycle => "competition_lifecycle",
        ClusterScheduleKind.LeaderboardRefresh => "leaderboard_refresh",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };
}

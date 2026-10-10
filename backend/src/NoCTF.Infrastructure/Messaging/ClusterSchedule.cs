using NoCTF.Application.Observability;

namespace NoCTF.Infrastructure.Messaging;

public enum ClusterScheduleKind
{
    AwdRound,
    AwdChecker,
    KohPoll,
    CompetitionLifecycle,
    AccountPrivacyRetention,
    RuntimeDispatch,
    GameplayFactRecovery,
    LiveSoloRound,
    LiveSoloMedia,
    LiveSoloCapture,
    ChallengeTiming
}

public sealed record ClusterScheduleEntry(
    string Key,
    ClusterScheduleKind Kind,
    DateTimeOffset DueAt,
    TimeSpan? Interval,
    object Message,
    long SkippedTicks = 0)
{
    public ClusterScheduleEntry At(DateTimeOffset dueAt) => this with
    {
        DueAt = dueAt,
        Message = ClusterScheduleMessageClock.MoveTo(Message, dueAt),
        SkippedTicks = 0
    };
}

public interface IClusterScheduleSource
{
    Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

/// <summary>Capability-owned scheduling sources compose without dependencies on other modes.</summary>
public interface IClusterScheduleContributor : IClusterScheduleSource;

public static class ClusterScheduleClock
{
    public static (DateTimeOffset DueAt, long SkippedTicks) ClampWithoutCatchUp(
        DateTimeOffset intendedDueAt,
        DateTimeOffset now,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        if (intendedDueAt >= now)
            return (intendedDueAt, 0);

        var elapsedTicks = now.UtcTicks - intendedDueAt.UtcTicks;
        return (now, Math.Max(0, elapsedTicks / interval.Ticks));
    }

    public static DateTimeOffset NextAfterDispatch(
        DateTimeOffset now,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        return now.Add(interval);
    }
}

public sealed class ClusterSchedulingState
{
    private readonly object sync = new();
    private ClusterSchedulingSnapshot snapshot = ClusterSchedulingSnapshot.Inactive;

    public ClusterSchedulingSnapshot Read()
    {
        lock (sync)
            return snapshot;
    }

    public void Activating(string ownerNode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerNode);
        lock (sync)
        {
            snapshot = snapshot with
            {
                IsActive = false,
                OwnerNode = ownerNode,
                LastError = null
            };
        }
    }

    public void Activated(
        string ownerNode,
        DateTimeOffset takenOverAt,
        DateTimeOffset rebuiltAt,
        TimeSpan rebuildDuration,
        int entryCount)
    {
        lock (sync)
        {
            snapshot = new(
                true,
                ownerNode,
                takenOverAt,
                rebuiltAt,
                rebuildDuration,
                entryCount,
                null);
        }
        NoCtfTelemetry.RecordSchedulerTakeover();
        NoCtfTelemetry.RecordSchedulerRebuild("success", rebuildDuration.TotalSeconds, entryCount);
    }

    public void Rebuilt(
        DateTimeOffset rebuiltAt,
        TimeSpan rebuildDuration,
        int entryCount)
    {
        lock (sync)
        {
            snapshot = snapshot with
            {
                LastRebuiltAt = rebuiltAt,
                LastRebuildDuration = rebuildDuration,
                EntryCount = entryCount,
                LastError = null
            };
        }
        NoCtfTelemetry.RecordSchedulerRebuild("success", rebuildDuration.TotalSeconds, entryCount);
    }

    public void Failed(string error, TimeSpan rebuildDuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        lock (sync)
        {
            snapshot = snapshot with
            {
                LastRebuildDuration = rebuildDuration,
                LastError = error
            };
        }
        NoCtfTelemetry.RecordSchedulerRebuild("failure", rebuildDuration.TotalSeconds, 0);
    }

    public void Stopped()
    {
        lock (sync)
            snapshot = snapshot with { IsActive = false };
    }
}

public sealed record ClusterSchedulingSnapshot(
    bool IsActive,
    string? OwnerNode,
    DateTimeOffset? LastTakenOverAt,
    DateTimeOffset? LastRebuiltAt,
    TimeSpan LastRebuildDuration,
    int EntryCount,
    string? LastError)
{
    public static ClusterSchedulingSnapshot Inactive { get; } = new(
        false,
        null,
        null,
        null,
        TimeSpan.Zero,
        0,
        null);
}

public sealed record ClusterSchedulerNodeIdentity(string Value);

internal static class ClusterScheduleMessageClock
{
    internal static object MoveTo(object message, DateTimeOffset dueAt) => message switch
    {
        NoCTF.Application.Messaging.AdvanceAwdRound value => value with { At = dueAt },
        NoCTF.Application.Messaging.DispatchAwdCheckers =>
            new NoCTF.Application.Messaging.DispatchAwdCheckers(dueAt),
        NoCTF.Application.Messaging.PollKohChallenge value => value.At(dueAt),
        NoCTF.Application.Messaging.AdvanceCompetitionLifecycle =>
            new NoCTF.Application.Messaging.AdvanceCompetitionLifecycle(dueAt),
        NoCTF.Application.Messaging.ExpireAccountSourceAddresses =>
            new NoCTF.Application.Messaging.ExpireAccountSourceAddresses(dueAt),
        NoCTF.Application.Messaging.DispatchQueuedRuntimes =>
            new NoCTF.Application.Messaging.DispatchQueuedRuntimes(dueAt),
        NoCTF.Application.Messaging.DispatchPendingGameplayFacts =>
            new NoCTF.Application.Messaging.DispatchPendingGameplayFacts(dueAt),
        NoCTF.Application.LiveSolo.Rounds.AdvanceLiveSoloRound value => value with { At = dueAt },
        NoCTF.Application.LiveSolo.Media.RefreshLiveSoloMedia value => value,
        NoCTF.Application.LiveSolo.Media.AdvanceLiveSoloCapture value => value,
        NoCTF.Application.LiveSolo.Media.PruneLiveSoloCapture value => value,
        NoCTF.Application.LiveSolo.Media.SnapshotLiveSoloResult value => value,
        NoCTF.Application.Challenges.Timing.AdvanceChallengeOpening value => value,
        NoCTF.Application.Challenges.Timing.RecalculateChallengeTiming value => value,
        _ => throw new ArgumentOutOfRangeException(
            nameof(message),
            message.GetType().FullName,
            "Unsupported cluster schedule message type.")
    };
}

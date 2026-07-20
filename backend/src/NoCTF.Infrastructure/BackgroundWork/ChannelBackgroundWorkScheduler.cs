using System.Collections.Concurrent;
using System.Threading.Channels;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.Infrastructure.BackgroundWork;

public abstract record ProcessingWorkItem;
public sealed record ProcessSubmissionWorkItem(Guid SubmissionId) : ProcessingWorkItem;
public sealed record ProcessSystemEventWorkItem(Guid ScoringEventId) : ProcessingWorkItem;
public sealed record RefreshLeaderboardWorkItem(Guid CompetitionId);
public abstract record MaintenanceWorkItem(Guid CompetitionId);
public sealed record RebuildCompetitionWorkItem(Guid CompetitionId) : MaintenanceWorkItem(CompetitionId);
public sealed record CleanupCompetitionRuntimeWorkItem(Guid CompetitionId) : MaintenanceWorkItem(CompetitionId);
public sealed record ProvisionCompetitionRuntimeWorkItem(Guid CompetitionId) : MaintenanceWorkItem(CompetitionId);

/// <summary>Bounded, single-host Channels used by the API background services.</summary>
public sealed class ChannelBackgroundWorkScheduler : IBackgroundWorkScheduler, IBackgroundWorkAdmissionGate, IDisposable
{
    private readonly Channel<ProcessingWorkItem> processing;
    private readonly Channel<RefreshLeaderboardWorkItem> projections;
    private readonly Channel<MaintenanceWorkItem> maintenance;
    private readonly ConcurrentDictionary<Guid, byte> pendingRefreshes = new();
    private readonly ConcurrentDictionary<Guid, byte> pendingRebuilds = new();
    private readonly ConcurrentDictionary<Guid, byte> pendingRuntimeCleanups = new();
    private readonly ConcurrentDictionary<Guid, byte> pendingRuntimeProvisions = new();
    private readonly CancellationTokenSource drainCancellation = new();
    private readonly TaskCompletionSource admissionDrained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int activeAdmissions;
    private int accepting = 1;

    public ChannelBackgroundWorkScheduler(BackgroundQueueOptions options)
    {
        processing = Create<ProcessingWorkItem>(options.ProcessingCapacity);
        projections = Create<RefreshLeaderboardWorkItem>(options.ProjectionCapacity);
        maintenance = Create<MaintenanceWorkItem>(options.MaintenanceCapacity);
    }

    public ChannelReader<ProcessingWorkItem> ProcessingReader => processing.Reader;
    public ChannelReader<RefreshLeaderboardWorkItem> ProjectionReader => projections.Reader;
    public ChannelReader<MaintenanceWorkItem> MaintenanceReader => maintenance.Reader;
    public bool IsAccepting => Volatile.Read(ref accepting) == 1;
    public CancellationToken DrainToken => drainCancellation.Token;

    public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
        WriteAsync(processing.Writer, new ProcessSubmissionWorkItem(submissionId), cancellationToken);

    public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) =>
        WriteAsync(processing.Writer, new ProcessSystemEventWorkItem(scoringEventId), cancellationToken);

    public async ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRefreshes.TryAdd(competitionId, 0)) return;
        try { await WriteAsync(projections.Writer, new(competitionId), cancellationToken); }
        catch { pendingRefreshes.TryRemove(competitionId, out _); throw; }
    }

    public async ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRebuilds.TryAdd(competitionId, 0)) return;
        try { await WriteAsync(maintenance.Writer, new RebuildCompetitionWorkItem(competitionId), cancellationToken); }
        catch { pendingRebuilds.TryRemove(competitionId, out _); throw; }
    }

    public async ValueTask EnqueueRuntimeCleanupAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRuntimeCleanups.TryAdd(competitionId, 0)) return;
        try { await WriteAsync(maintenance.Writer, new CleanupCompetitionRuntimeWorkItem(competitionId), cancellationToken); }
        catch { pendingRuntimeCleanups.TryRemove(competitionId, out _); throw; }
    }

    public async ValueTask EnqueueRuntimeProvisionAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRuntimeProvisions.TryAdd(competitionId, 0)) return;
        try { await WriteAsync(maintenance.Writer, new ProvisionCompetitionRuntimeWorkItem(competitionId), cancellationToken); }
        catch { pendingRuntimeProvisions.TryRemove(competitionId, out _); throw; }
    }

    public void CompleteRefresh(Guid competitionId) => pendingRefreshes.TryRemove(competitionId, out _);
    public void CompleteRebuild(Guid competitionId) => pendingRebuilds.TryRemove(competitionId, out _);
    public void CompleteRuntimeCleanup(Guid competitionId) => pendingRuntimeCleanups.TryRemove(competitionId, out _);
    public void CompleteRuntimeProvision(Guid competitionId) => pendingRuntimeProvisions.TryRemove(competitionId, out _);

    public IBackgroundWorkAdmissionLease? TryEnter()
    {
        if (!IsAccepting) return null;

        Interlocked.Increment(ref activeAdmissions);
        if (IsAccepting)
            return new AdmissionLease(this, drainCancellation.Token);

        ReleaseAdmission();
        return null;
    }

    public void BeginShutdown()
    {
        if (Interlocked.Exchange(ref accepting, 0) == 0) return;
        if (Volatile.Read(ref activeAdmissions) == 0)
            admissionDrained.TrySetResult();
    }

    public void BeginDrain(TimeSpan drainTimeout) => drainCancellation.CancelAfter(drainTimeout);

    public void CancelDrain() => drainCancellation.Cancel();

    public Task WaitForAdmissionsToDrainAsync(CancellationToken cancellationToken) =>
        admissionDrained.Task.WaitAsync(cancellationToken);

    public void CompleteMaintenanceWriter() => maintenance.Writer.TryComplete();

    public void CompleteProcessingWriters()
    {
        processing.Writer.TryComplete();
    }

    public void CompleteProjectionWriter() => projections.Writer.TryComplete();

    public void CompleteAllWriters()
    {
        CompleteMaintenanceWriter();
        CompleteProcessingWriters();
        CompleteProjectionWriter();
    }

    private async ValueTask WriteAsync<T>(ChannelWriter<T> writer, T item, CancellationToken cancellationToken)
    {
        try
        {
            await writer.WriteAsync(item, cancellationToken);
        }
        catch (ChannelClosedException exception)
        {
            throw new BackgroundWorkUnavailableException(exception);
        }
    }

    private void ReleaseAdmission()
    {
        if (Interlocked.Decrement(ref activeAdmissions) == 0 && !IsAccepting)
            admissionDrained.TrySetResult();
    }

    public void Dispose() => drainCancellation.Dispose();

    private static Channel<T> Create<T>(int capacity) => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = false
    });

    private sealed class AdmissionLease(ChannelBackgroundWorkScheduler owner, CancellationToken drainCancellation)
        : IBackgroundWorkAdmissionLease
    {
        private int disposed;
        public CancellationToken DrainCancellation { get; } = drainCancellation;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.ReleaseAdmission();
        }
    }
}

public sealed class BackgroundQueueOptions
{
    public const string SectionName = "BackgroundQueue";
    public int ProcessingCapacity { get; init; } = 256;
    public int ProcessingConcurrency { get; init; } = 4;
    public int ProjectionCapacity { get; init; } = 64;
    public int ProjectionConcurrency { get; init; } = 1;
    public int MaintenanceCapacity { get; init; } = 16;
    public int MaintenanceConcurrency { get; init; } = 1;
    public int ShutdownDrainSeconds { get; init; } = 20;
}

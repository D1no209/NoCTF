using System.Collections.Concurrent;
using System.Threading.Channels;
using NoCTF.Application.BackgroundWork;

namespace NoCTF.Infrastructure.BackgroundWork;

public sealed record ProcessSubmissionWorkItem(Guid SubmissionId);
public sealed record ProcessSystemEventWorkItem(Guid ScoringEventId);
public sealed record RefreshLeaderboardWorkItem(Guid CompetitionId);
public sealed record RebuildCompetitionWorkItem(Guid CompetitionId);

/// <summary>Bounded, single-host Channels used by the API background services.</summary>
public sealed class ChannelBackgroundWorkScheduler : IBackgroundWorkScheduler
{
    private readonly Channel<ProcessSubmissionWorkItem> processing;
    private readonly Channel<ProcessSystemEventWorkItem> systemEvents;
    private readonly Channel<RefreshLeaderboardWorkItem> projections;
    private readonly Channel<RebuildCompetitionWorkItem> maintenance;
    private readonly ConcurrentDictionary<Guid, byte> pendingRefreshes = new();
    private readonly ConcurrentDictionary<Guid, byte> pendingRebuilds = new();

    public ChannelBackgroundWorkScheduler(BackgroundQueueOptions options)
    {
        processing = Create<ProcessSubmissionWorkItem>(options.ProcessingCapacity);
        systemEvents = Create<ProcessSystemEventWorkItem>(options.ProcessingCapacity);
        projections = Create<RefreshLeaderboardWorkItem>(options.ProjectionCapacity);
        maintenance = Create<RebuildCompetitionWorkItem>(options.MaintenanceCapacity);
    }

    public ChannelReader<ProcessSubmissionWorkItem> ProcessingReader => processing.Reader;
    public ChannelReader<ProcessSystemEventWorkItem> SystemEventReader => systemEvents.Reader;
    public ChannelReader<RefreshLeaderboardWorkItem> ProjectionReader => projections.Reader;
    public ChannelReader<RebuildCompetitionWorkItem> MaintenanceReader => maintenance.Reader;

    public ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
        processing.Writer.WriteAsync(new(submissionId), cancellationToken);

    public ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken) =>
        systemEvents.Writer.WriteAsync(new(scoringEventId), cancellationToken);

    public async ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRefreshes.TryAdd(competitionId, 0)) return;
        try { await projections.Writer.WriteAsync(new(competitionId), cancellationToken); }
        catch { pendingRefreshes.TryRemove(competitionId, out _); throw; }
    }

    public async ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken)
    {
        if (!pendingRebuilds.TryAdd(competitionId, 0)) return;
        try { await maintenance.Writer.WriteAsync(new(competitionId), cancellationToken); }
        catch { pendingRebuilds.TryRemove(competitionId, out _); throw; }
    }

    public void CompleteRefresh(Guid competitionId) => pendingRefreshes.TryRemove(competitionId, out _);
    public void CompleteRebuild(Guid competitionId) => pendingRebuilds.TryRemove(competitionId, out _);

    private static Channel<T> Create<T>(int capacity) => Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleWriter = false,
        SingleReader = false
    });
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
}

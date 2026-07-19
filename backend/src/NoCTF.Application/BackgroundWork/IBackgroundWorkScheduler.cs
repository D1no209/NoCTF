namespace NoCTF.Application.BackgroundWork;

/// <summary>Schedules in-process background work without exposing the transport implementation.</summary>
public interface IBackgroundWorkScheduler
{
    ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);
    ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken);
    ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken);
}

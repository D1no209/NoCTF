namespace NoCTF.Application.BackgroundWork;

/// <summary>Schedules in-process background work without exposing the transport implementation.</summary>
public interface IBackgroundWorkScheduler
{
    ValueTask EnqueueSubmissionAsync(Guid submissionId, CancellationToken cancellationToken);
    ValueTask EnqueueSystemEventAsync(Guid scoringEventId, CancellationToken cancellationToken);
    ValueTask EnqueueLeaderboardRefreshAsync(Guid competitionId, CancellationToken cancellationToken);
    ValueTask EnqueueCompetitionRebuildAsync(Guid competitionId, CancellationToken cancellationToken);
    ValueTask EnqueueRuntimeCleanupAsync(Guid competitionId, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>Guards the transaction-to-enqueue boundary while the host enters graceful shutdown.</summary>
public interface IBackgroundWorkAdmissionGate
{
    bool IsAccepting { get; }
    IBackgroundWorkAdmissionLease? TryEnter();
}

public interface IBackgroundWorkAdmissionLease : IDisposable
{
    CancellationToken DrainCancellation { get; }
}

public sealed class BackgroundWorkUnavailableException(Exception? innerException = null)
    : InvalidOperationException("The in-process background work scheduler is not accepting work.", innerException);

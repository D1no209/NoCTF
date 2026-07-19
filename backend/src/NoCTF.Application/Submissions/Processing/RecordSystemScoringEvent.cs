using NoCTF.Application.BackgroundWork;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public sealed record RecordSystemScoringEventCommand(
    Guid CompetitionId,
    Guid? TeamId,
    Guid? ChallengeId,
    ScoringEventKind Kind,
    ScoringResult Result,
    ScoringFailureCode? FailureCode,
    DateTimeOffset OccurredAt,
    string EvaluatorVersion,
    string SourceKey);

public sealed record RecordSystemScoringEventResult(Guid ScoringEventId, bool Created);

public interface ISystemScoringEventStore
{
    Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken cancellationToken);
}

public sealed class RecordSystemScoringEvent(
    ISystemScoringEventStore store,
    ILeaderboardCache cache,
    IBackgroundWorkScheduler scheduler)
{
    public async Task<RecordSystemScoringEventResult> ExecuteAsync(
        RecordSystemScoringEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.EvaluatorVersion);
        if (command.Kind == ScoringEventKind.SubmissionEvaluation)
            throw new ArgumentException("System events cannot use the submission evaluation kind.", nameof(command));

        var result = await store.RecordAsync(command, cancellationToken);
        if (!result.Created) return result;

        await cache.InvalidateAsync(command.CompetitionId, cancellationToken);
        await scheduler.EnqueueLeaderboardRefreshAsync(command.CompetitionId, cancellationToken);
        return result;
    }
}

using NoCTF.Application.BackgroundWork;
using NoCTF.Domain.Submissions;
using NoCTF.Domain.Competitions;

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

public enum SystemScoringEventRecordFailure
{
    CompetitionNotFound,
    CompetitionFinished
}

public sealed record RecordSystemScoringEventResult(
    Guid ScoringEventId,
    bool Created,
    SystemScoringEventRecordFailure? Failure = null);

public interface ISystemScoringEventStore
{
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<RecordSystemScoringEventResult> RecordAsync(RecordSystemScoringEventCommand command, CancellationToken cancellationToken);
}

public interface ISystemScoringEventProcessor
{
    Task ProcessAsync(Guid scoringEventId, CancellationToken cancellationToken);
}

public sealed class RecordSystemScoringEvent(
    ISystemScoringEventStore store,
    IBackgroundWorkScheduler scheduler,
    IBackgroundWorkAdmissionGate admissionGate)
{
    public async Task<RecordSystemScoringEventResult> ExecuteAsync(
        RecordSystemScoringEventCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.EvaluatorVersion);
        if (command.Kind == ScoringEventKind.SubmissionEvaluation)
            throw new ArgumentException("System events cannot use the submission evaluation kind.", nameof(command));

        var status = await store.GetCompetitionStatusAsync(command.CompetitionId, cancellationToken);
        if (status is null)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionNotFound);
        if (status == CompetitionStatus.Finished)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionFinished);

        using var admission = admissionGate.TryEnter();
        if (admission is null)
            throw new BackgroundWorkUnavailableException();

        var result = await store.RecordAsync(command, cancellationToken);
        if (!result.Created) return result;

        try
        {
            await scheduler.EnqueueSystemEventAsync(result.ScoringEventId, admission.DrainCancellation);
        }
        catch (OperationCanceledException) when (admission.DrainCancellation.IsCancellationRequested)
        {
            throw new BackgroundWorkUnavailableException();
        }
        return result;
    }
}

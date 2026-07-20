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
    CompetitionFinished,
    CompetitionModeMismatch,
    SourceConflict
}

public sealed record RecordSystemScoringEventResult(
    Guid ScoringEventId,
    bool Created,
    SystemScoringEventRecordFailure? Failure = null);

public interface ISystemScoringEventStore
{
    Task<CompetitionStatus?> GetCompetitionStatusAsync(Guid competitionId, CancellationToken cancellationToken);
    Task<GameMode?> GetCompetitionModeAsync(Guid competitionId, CancellationToken cancellationToken);
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
        if (command.Kind == ScoringEventKind.AwdpFixCheck)
            throw new ArgumentException("AWDP fix checks require the dedicated checker-result use case.", nameof(command));
        return await ExecuteCoreAsync(command, cancellationToken);
    }

    public async Task<RecordSystemScoringEventResult> ExecuteAwdpCheckAsync(
        RecordSystemScoringEventCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidAwdpCheck(command))
            throw new ArgumentException("Invalid AWDP service-check fact.", nameof(command));
        return await ExecuteCoreAsync(command, cancellationToken);
    }

    private async Task<RecordSystemScoringEventResult> ExecuteCoreAsync(
        RecordSystemScoringEventCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.EvaluatorVersion);
        if (command.Kind == ScoringEventKind.SubmissionEvaluation)
            throw new ArgumentException("System events cannot use the submission evaluation kind.", nameof(command));
        if (command.Kind != ScoringEventKind.AwdpFixCheck
            && command.FailureCode is ScoringFailureCode.AwdpFixFailed
                or ScoringFailureCode.AwdpServiceDown or ScoringFailureCode.AwdpViolation)
            throw new ArgumentException("AWDP check failure codes require an AWDP service-check event.", nameof(command));

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

    private static bool IsValidAwdpCheck(RecordSystemScoringEventCommand command) => command is
        { Kind: ScoringEventKind.AwdpFixCheck, Result: ScoringResult.Correct, FailureCode: null }
        or { Kind: ScoringEventKind.AwdpFixCheck, Result: ScoringResult.Wrong,
            FailureCode: ScoringFailureCode.AwdpFixFailed or ScoringFailureCode.AwdpServiceDown }
        or { Kind: ScoringEventKind.AwdpFixCheck, Result: ScoringResult.Rejected,
            FailureCode: ScoringFailureCode.AwdpViolation }
        or { Kind: ScoringEventKind.AwdpFixCheck, Result: ScoringResult.PlatformFailed,
            FailureCode: ScoringFailureCode.CheckerPlatformError };
}

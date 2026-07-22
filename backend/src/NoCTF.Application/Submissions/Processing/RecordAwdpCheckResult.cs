using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public enum AwdpCheckOutcome
{
    FixSuccess,
    FixFailed,
    FixRuleViolation,
    FixServiceError,
    PlatformFailure
}

public enum AwdpVerificationPhase
{
    Patch,
    Checker
}

public sealed record AwdpCheckDecision(
    AwdpCheckOutcome Outcome,
    ScoringResult Result,
    ScoringFailureCode? FailureCode);

public interface IAwdpCheckExitCodeMapper
{
    AwdpCheckDecision Map(int exitCode, bool timedOut);
}

public sealed record RecordAwdpCheckResultCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid SubmissionId,
    AwdpVerificationPhase Phase,
    int ExitCode,
    bool TimedOut,
    DateTimeOffset OccurredAt,
    string SourceKey);

public sealed class RecordAwdpCheckResult(
    IAwdpCheckExitCodeMapper mapper,
    ISystemScoringEventStore store,
    RecordSystemScoringEvent record)
{
    public Task<RecordSystemScoringEventResult> ExecuteAsync(
        RecordAwdpCheckResultCommand command,
        CancellationToken cancellationToken = default)
    {
        return ExecuteCoreAsync(command, cancellationToken);
    }

    private async Task<RecordSystemScoringEventResult> ExecuteCoreAsync(
        RecordAwdpCheckResultCommand command,
        CancellationToken cancellationToken)
    {
        var mode = await store.GetCompetitionModeAsync(command.CompetitionId, cancellationToken);
        if (mode is null)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionNotFound);
        if (mode is not NoCTF.Domain.Competitions.GameMode.Awdp)
            return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionModeMismatch);
        var decision = command.Phase == AwdpVerificationPhase.Patch
            ? MapPatch(command.ExitCode, command.TimedOut)
            : mapper.Map(command.ExitCode, command.TimedOut);
        return await record.ExecuteAwdpCheckAsync(new(
            command.CompetitionId,
            command.TeamId,
            command.CompetitionChallengeId,
            ScoringEventKind.AwdpFixCheck,
            decision.Result,
            decision.FailureCode,
            command.OccurredAt,
            "awdp-check-v1",
            command.SourceKey,
            SubmissionId: command.SubmissionId), cancellationToken);
    }


    private static AwdpCheckDecision MapPatch(int exitCode, bool timedOut) => timedOut
        ? new(AwdpCheckOutcome.FixRuleViolation, ScoringResult.Rejected, ScoringFailureCode.AwdpPatchTimeout)
        : exitCode == 0
            ? new(AwdpCheckOutcome.FixSuccess, ScoringResult.Correct, null)
            : new(AwdpCheckOutcome.FixRuleViolation, ScoringResult.Rejected, ScoringFailureCode.AwdpPatchFailed);
}

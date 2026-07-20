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
    Guid ChallengeId,
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
        var decision = mapper.Map(command.ExitCode, command.TimedOut);
        return await record.ExecuteAwdpCheckAsync(new(
            command.CompetitionId,
            command.TeamId,
            command.ChallengeId,
            ScoringEventKind.AwdpFixCheck,
            decision.Result,
            decision.FailureCode,
            command.OccurredAt,
            "awdp-check-v1",
            command.SourceKey), cancellationToken);
    }
}

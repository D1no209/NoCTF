using NoCTF.Domain.Competitions;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.Submissions.Processing;

public sealed record RecordAwdCheckResultCommand(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    int ExitCode,
    bool TimedOut,
    DateTimeOffset OccurredAt,
    string SourceKey);

public sealed class RecordAwdCheckResult(
    ISystemScoringEventStore store,
    RecordSystemScoringEvent record)
{
    public async Task<RecordSystemScoringEventResult> ExecuteAsync(
        RecordAwdCheckResultCommand command,
        CancellationToken cancellationToken = default)
    {
        var mode = await store.GetCompetitionModeAsync(command.CompetitionId, cancellationToken);
        if (mode is null) return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionNotFound);
        if (mode is not GameMode.Awd) return new(Guid.Empty, false, SystemScoringEventRecordFailure.CompetitionModeMismatch);
        var result = command.TimedOut
            ? ScoringResult.PlatformFailed
            : command.ExitCode == 0 ? ScoringResult.Correct : ScoringResult.Wrong;
        ScoringFailureCode? failure = command.TimedOut ? ScoringFailureCode.ProducerTimeout : null;
        return await record.ExecuteAsync(new(
            command.CompetitionId,
            command.TeamId,
            command.CompetitionChallengeId,
            ScoringEventKind.AwdServiceCheck,
            result,
            failure,
            command.OccurredAt,
            "awd-checker-v1",
            command.SourceKey), cancellationToken);
    }
}

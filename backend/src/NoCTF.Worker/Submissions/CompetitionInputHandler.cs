using NoCTF.Application.Scoring.Ports;
using NoCTF.Application.Submissions.Ports;

namespace NoCTF.Worker.Submissions;

public sealed class CompetitionInputHandler(
    ICompetitionInputAppender appender,
    IScoringRebuildQueue rebuildQueue)
{
    public Task Handle(AwdFlagInput input, CancellationToken cancellationToken) =>
        AppendAndRebuildAsync(input.CompetitionId, ScoringRebuildReason.AwdFlagRotated,
            () => appender.AppendAsync(input, cancellationToken), cancellationToken);

    public Task Handle(AwdServiceInput input, CancellationToken cancellationToken) =>
        AppendAndRebuildAsync(input.CompetitionId, ScoringRebuildReason.AwdServiceChecked,
            () => appender.AppendAsync(input, cancellationToken), cancellationToken);

    public Task Handle(KohInput input, CancellationToken cancellationToken) =>
        AppendAndRebuildAsync(input.CompetitionId, ScoringRebuildReason.KohControlObserved,
            () => appender.AppendAsync(input, cancellationToken), cancellationToken);

    public Task Handle(SystemScoreInput input, CancellationToken cancellationToken) =>
        AppendAndRebuildAsync(input.CompetitionId, ScoringRebuildReason.SystemScoringInput,
            () => appender.AppendAsync(input, cancellationToken), cancellationToken);

    public Task Handle(PenetrationStageInput input, CancellationToken cancellationToken) =>
        AppendAndRebuildAsync(input.CompetitionId, ScoringRebuildReason.PenetrationStageCompleted,
            () => appender.AppendAsync(input, cancellationToken), cancellationToken);

    private async Task AppendAndRebuildAsync(
        Guid competitionId,
        ScoringRebuildReason reason,
        Func<Task<bool>> append,
        CancellationToken cancellationToken)
    {
        if (await append())
            await rebuildQueue.EnqueueAsync(competitionId, reason, cancellationToken);
    }
}

using NoCTF.Application.Submissions.Processing;
using NoCTF.Domain.Submissions;

namespace NoCTF.Application.SystemProducers;

public sealed record PenetrationStageMonitorTarget(
    Guid CompetitionId,
    Guid TeamId,
    Guid CompetitionChallengeId,
    Guid ChallengeInstanceId,
    DateTimeOffset FailedAt,
    string ChallengeConfigurationJson,
    IReadOnlySet<Guid> CompletedStageIds);

public interface IPenetrationStageMonitorTargetStore
{
    Task<IReadOnlyList<PenetrationStageMonitorTarget>> ListFailedAsync(
        CancellationToken cancellationToken);
}

public interface IPenetrationStageConfigurationCatalog
{
    PenetrationStageProgress GetProgress(
        string challengeConfigurationJson,
        IReadOnlySet<Guid> completedStageIds);
}

public sealed record PenetrationStageProgress(
    int TotalStageCount,
    IReadOnlyList<Guid> RemainingStageIds)
{
    public bool AllCompleted => TotalStageCount > 0 && RemainingStageIds.Count == 0;
}

public sealed class MonitorPenetrationStages(
    IPenetrationStageMonitorTargetStore targets,
    IPenetrationStageConfigurationCatalog configurations,
    RecordSystemScoringEvent record)
{
    public async Task<SystemProducerSweepResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var targetCount = 0;
        var createdCount = 0;
        var failedCount = 0;
        foreach (var target in await targets.ListFailedAsync(cancellationToken))
        {
            targetCount++;
            try
            {
                var progress = configurations.GetProgress(
                    target.ChallengeConfigurationJson,
                    target.CompletedStageIds);
                if (progress.AllCompleted) continue;
                foreach (var stageId in progress.RemainingStageIds)
                {
                    var command = new RecordSystemScoringEventCommand(
                        target.CompetitionId,
                        target.TeamId,
                        target.CompetitionChallengeId,
                        ScoringEventKind.PenetrationStage,
                        ScoringResult.PlatformFailed,
                        ScoringFailureCode.ProducerUnavailable,
                        target.FailedAt,
                        "penetration-runtime-monitor-v1",
                        $"penetration:{target.ChallengeInstanceId:N}:stage:{stageId:N}:runtime-failed",
                        stageId,
                        target.ChallengeInstanceId);
                    var result = await record.ExecutePenetrationStageAsync(command, cancellationToken);
                    if (result.Created) createdCount++;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                failedCount++;
            }
        }
        return new(targetCount, createdCount, failedCount);
    }
}

using NoCTF.Application.Runtime;
using NoCTF.Domain.Competitions;

namespace NoCTF.Application.SystemProducers;

public interface IRunningCompetitionStore
{
    Task<IReadOnlyList<Guid>> ListRunningAsync(GameMode mode, CancellationToken cancellationToken);
}

public sealed record ModeRuntimeSweepResult(int CompetitionCount, int ProvisionedCount, int FailedCount);

public sealed class ProvisionModeRuntimes(
    IRunningCompetitionStore competitions,
    CompetitionRuntimeProvisioner provisioner)
{
    public async Task<ModeRuntimeSweepResult> ExecuteAsync(GameMode mode, CancellationToken cancellationToken = default)
    {
        var competitionIds = await competitions.ListRunningAsync(mode, cancellationToken);
        var provisioned = 0;
        var failed = 0;
        foreach (var competitionId in competitionIds)
        {
            var result = await provisioner.ExecuteAsync(competitionId, cancellationToken);
            provisioned += result.ProvisionedCount;
            failed += result.FailedCount;
        }
        return new(competitionIds.Count, provisioned, failed);
    }
}

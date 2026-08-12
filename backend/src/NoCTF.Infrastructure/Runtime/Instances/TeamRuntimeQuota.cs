using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class TeamRuntimeQuota(LocalCriticalSectionRegistry localLeases)
{
    public async ValueTask<IAsyncDisposable> AcquireLockAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await localLeases.AcquireAsync(
                "team-runtime-quota", $"{competitionId:N}:{teamId:N}", cancellationToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var affected = await db.Teams
                .Where(team => team.Id == teamId && team.CompetitionId == competitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    team => team.CriticalSectionVersion,
                    team => team.CriticalSectionVersion + 1), budget.Token);
            if (affected != 1)
                throw new DbUpdateConcurrencyException("The runtime quota team no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("team-runtime-quota");
        }
    }

    public async Task<bool> CanCreateSlotAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        Guid competitionChallengeId,
        int maximumSlots,
        CancellationToken cancellationToken)
    {
        if (maximumSlots <= 0)
            return true;

        var activeChallengeIds = await db.RuntimeInstances.AsNoTracking()
            .Where(runtime =>
                runtime.CompetitionId == competitionId &&
                runtime.TeamId == teamId &&
                (runtime.Purpose == RuntimePurpose.Player
                    || runtime.Purpose == RuntimePurpose.Practice) &&
                (runtime.State == RuntimeState.Queued ||
                 runtime.State == RuntimeState.Provisioning ||
                 runtime.State == RuntimeState.Running ||
                 runtime.State == RuntimeState.Stopping))
            .Select(runtime => runtime.CompetitionChallengeId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        return activeChallengeIds.Contains(competitionChallengeId)
            || activeChallengeIds.Length < maximumSlots;
    }

    internal static string LockKey(Guid competitionId, Guid teamId) =>
        $"team-runtime:{competitionId:N}:{teamId:N}";
}

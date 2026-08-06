using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

internal static class TeamRuntimeQuota
{
    public static ValueTask<IAsyncDisposable> AcquireLockAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken)
        => CriticalSectionCoordinator.AcquireAsync(
            db,
            $"team-runtime:{competitionId:N}:{teamId:N}",
            token => db.Teams
                .Where(team => team.Id == teamId && team.CompetitionId == competitionId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    team => team.CriticalSectionVersion,
                    team => team.CriticalSectionVersion + 1), token),
            cancellationToken);

    public static async Task<bool> CanCreateSlotAsync(
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
                runtime.Purpose == RuntimePurpose.Player &&
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

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class TeamRuntimeQuota(
    AsyncKeyedLock.AsyncKeyedLocker<string> localLeases)
{
    public async ValueTask<IDisposable> AcquireLockAsync(
        NoCtfDbContext db,
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await localLeases.LockOrNullAsync(
                    $"team-runtime-quota:{competitionId:N}:{teamId:N}",
                    TimeSpan.FromSeconds(2), cancellationToken)
                ?? throw new FeatureCriticalSectionTimeoutException("team-runtime-quota");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await NoCTF.Infrastructure.Competitions.Participation.CompetitionParticipationLock.AcquireAsync(db, competitionId, cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.Teams
                .FromSqlInterpolated($"SELECT * FROM teams WHERE id = {teamId} AND competition_id = {competitionId} FOR UPDATE")
                .AsNoTracking()
                .AnyAsync(budget.Token);
            if (!exists)
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
                    || runtime.Purpose == RuntimePurpose.Practice
                    || runtime.Purpose == RuntimePurpose.AwdpAttack
                    || runtime.Purpose == RuntimePurpose.PatchVerificationTarget) &&
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

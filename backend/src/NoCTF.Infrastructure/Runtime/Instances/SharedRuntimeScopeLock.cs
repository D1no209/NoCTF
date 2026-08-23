using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class SharedRuntimeCriticalSection(
    AsyncKeyedLock.AsyncKeyedLocker<string> localLeases)
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await localLeases.LockOrNullAsync(
                    $"shared-runtime:{competitionChallengeId:N}",
                    TimeSpan.FromSeconds(2), cancellationToken)
                ?? throw new FeatureCriticalSectionTimeoutException("shared-runtime");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.CompetitionChallenges
                .FromSqlInterpolated($"SELECT * FROM competition_challenges WHERE id = {competitionChallengeId} FOR UPDATE")
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(budget.Token);
            if (!exists)
                throw new DbUpdateConcurrencyException("The shared runtime scope no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("shared-runtime");
        }
    }

    internal static string LockKey(Guid competitionChallengeId) =>
        $"{competitionChallengeId}:shared";
}

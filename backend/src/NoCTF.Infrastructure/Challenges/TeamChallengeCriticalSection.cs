using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

public sealed class TeamChallengeCriticalSection(
    AsyncKeyedLock.AsyncKeyedLocker<string> localLeases)
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return await localLeases.LockOrNullAsync(
                    $"team-challenge:{teamId:N}:{competitionChallengeId:N}",
                    TimeSpan.FromSeconds(2), cancellationToken)
                ?? throw new FeatureCriticalSectionTimeoutException("team-challenge");
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.Teams
                .FromSqlInterpolated($"SELECT * FROM teams WHERE id = {teamId} FOR UPDATE")
                .AsNoTracking()
                .AnyAsync(budget.Token);
            if (!exists)
                throw new DbUpdateConcurrencyException("The team challenge scope no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("team-challenge");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

public sealed class GameplayFactAttemptCriticalSection(
    AsyncKeyedLock.AsyncKeyedLocker<string> localLeases)
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        GameplayFactKind kind,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return await localLeases.LockOrNullAsync(
                    $"submission-attempt:{teamId:N}:{competitionChallengeId:N}:{(short)kind}",
                    TimeSpan.FromSeconds(2), cancellationToken)
                ?? throw new FeatureCriticalSectionTimeoutException("submission-attempt");
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
                throw new DbUpdateConcurrencyException("The submission team no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("submission-attempt");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class SharedRuntimeCriticalSection
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.CompetitionChallenges
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(item => item.Id == competitionChallengeId, budget.Token);
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

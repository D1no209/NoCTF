using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

public sealed class SharedRuntimeCriticalSection(LocalCriticalSectionRegistry localLeases)
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
            return await localLeases.AcquireAsync(
                "shared-runtime", $"{competitionChallengeId:N}", cancellationToken);

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var affected = await db.CompetitionChallenges.IgnoreQueryFilters()
                .Where(challenge => challenge.Id == competitionChallengeId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    challenge => challenge.CriticalSectionVersion,
                    challenge => challenge.CriticalSectionVersion + 1), budget.Token);
            if (affected != 1)
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

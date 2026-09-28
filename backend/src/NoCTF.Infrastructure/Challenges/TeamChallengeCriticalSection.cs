using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

public sealed class TeamChallengeCriticalSection
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var exists = await db.Teams
                .AsNoTracking()
                .AnyAsync(team => team.Id == teamId, budget.Token);
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

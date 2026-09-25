using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Gameplay;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Intake;

public sealed class GameplayFactAttemptCriticalSection
{
    public async ValueTask<IDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        GameplayFactKind kind,
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
                throw new DbUpdateConcurrencyException("The submission team no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("submission-attempt");
        }
    }
}

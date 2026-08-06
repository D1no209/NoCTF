using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

public sealed class TeamChallengeCriticalSection(LocalCriticalSectionRegistry localLeases)
{
    public async ValueTask<IAsyncDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid teamId,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return await localLeases.AcquireAsync(
                "team-challenge",
                $"{teamId:N}:{competitionChallengeId:N}",
                cancellationToken);
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var affected = await db.Teams.Where(team => team.Id == teamId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    team => team.CriticalSectionVersion,
                    team => team.CriticalSectionVersion + 1), budget.Token);
            if (affected != 1)
                throw new DbUpdateConcurrencyException("The team challenge scope no longer exists.");
            return NoopCriticalSectionLease.Instance;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new FeatureCriticalSectionTimeoutException("team-challenge");
        }
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

internal static class SharedRuntimeScopeLock
{
    public static ValueTask<IAsyncDisposable> AcquireAsync(
        NoCtfDbContext db,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
        => CriticalSectionCoordinator.AcquireAsync(
            db,
            $"shared-runtime:{competitionChallengeId:N}",
            token => db.CompetitionChallenges.IgnoreQueryFilters()
                .Where(challenge => challenge.Id == competitionChallengeId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(
                    challenge => challenge.CriticalSectionVersion,
                    challenge => challenge.CriticalSectionVersion + 1), token),
            cancellationToken);

    internal static string LockKey(Guid competitionChallengeId) =>
        $"{competitionChallengeId}:shared";
}

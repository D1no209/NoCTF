using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

internal static class ChallengeWriteLock
{
    public static async Task AcquireAsync(
        NoCtfDbContext db,
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        var challenge = await db.Challenges.IgnoreQueryFilters()
            .SingleOrDefaultAsync(candidate => candidate.Id == challengeId, cancellationToken);
        if (challenge is not null)
            challenge.ConcurrencyVersion = checked(challenge.ConcurrencyVersion + 1);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

internal static class RuntimeFailurePersistence
{
    public static async Task InvalidateFlagsAsync(
        NoCtfDbContext db,
        Guid challengeInstanceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await db.ChallengeFlags
            .Where(flag => flag.ChallengeInstanceId == challengeInstanceId
                           && flag.Status == ChallengeFlagStatus.PendingInjection)
            .ExecuteUpdateAsync(update => update
                .SetProperty(flag => flag.InjectionClaimToken, (Guid?)null)
                .SetProperty(flag => flag.InjectionClaimedAt, (DateTimeOffset?)null)
                .SetProperty(flag => flag.UpdatedAt, now)
                .SetProperty(flag => flag.RowVersion, flag => flag.RowVersion + 1), cancellationToken);
        await db.ChallengeFlags
            .Where(flag => flag.ChallengeInstanceId == challengeInstanceId
                           && flag.Status == ChallengeFlagStatus.Active
                           && (flag.ValidEnd == null || flag.ValidEnd > now))
            .ExecuteUpdateAsync(update => update
                .SetProperty(flag => flag.ValidEnd, now)
                .SetProperty(flag => flag.UpdatedAt, now)
                .SetProperty(flag => flag.RowVersion, flag => flag.RowVersion + 1), cancellationToken);
    }
}

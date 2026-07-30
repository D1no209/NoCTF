using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

internal static class ChallengeWriteLock
{
    public static Task AcquireAsync(
        NoCtfDbContext db,
        Guid challengeId,
        CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'h' + challengeId.ToString("N")}, 0))",
            cancellationToken);
}

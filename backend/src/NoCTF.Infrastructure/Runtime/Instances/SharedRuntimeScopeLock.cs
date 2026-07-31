using Microsoft.EntityFrameworkCore;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Runtime.Instances;

internal static class SharedRuntimeScopeLock
{
    public static Task AcquireAsync(
        NoCtfDbContext db,
        Guid competitionChallengeId,
        CancellationToken cancellationToken)
    {
        var key = LockKey(competitionChallengeId);
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))",
            cancellationToken);
    }

    internal static string LockKey(Guid competitionChallengeId) =>
        $"{competitionChallengeId}:shared";
}

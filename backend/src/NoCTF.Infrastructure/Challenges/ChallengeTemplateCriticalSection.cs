using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges;

public static class ChallengeTemplateCriticalSection
{
    public static Task<Challenge?> AcquireAsync(
        NoCtfDbContext db,
        Guid challengeId,
        CancellationToken cancellationToken) =>
        db.Challenges
            .FromSqlInterpolated($"SELECT * FROM challenges WHERE id = {challengeId} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(cancellationToken);
}

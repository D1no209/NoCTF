using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

internal static class GameplayChallengeDefinitionReader
{
    public static async Task<ChallengeDefinition?> ReadAsync(NoCtfDbContext db, Guid challengeId,
        GameMode mode, CancellationToken ct)
    {
        if (mode == GameMode.Ctf)
        {
            // Both omitted navigations are verified null by this query. Keep the
            // complete static definition, including all ordered string items.
            var staticDefinition = await db.Set<CtfChallengeDefinition>().AsNoTracking().IgnoreAutoIncludes()
                .Where(item => item.ChallengeId == challengeId && item.Runtime == null && item.Checker == null
                    && db.Challenges.Any(template => template.Id == item.ChallengeId))
                .Include(item => item.StringItems).AsSplitQuery().SingleOrDefaultAsync(ct);
            if (staticDefinition is not null) return staticDefinition;
        }
        return await db.Challenges.AsNoTracking().AsSplitQuery().Where(item => item.Id == challengeId)
            .Select(item => item.Definition).SingleAsync(ct);
    }
}

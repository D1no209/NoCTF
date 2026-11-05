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
        return await db.Challenges.AsNoTracking().AsSplitQuery().Where(item => item.Id == challengeId)
            .Select(item => item.Definition).SingleAsync(ct);
    }
}

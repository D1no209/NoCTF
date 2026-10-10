using Microsoft.EntityFrameworkCore;
using NoCTF.Application.GameplayFacts.Processing;
using NoCTF.Application.LiveSolo.Matches;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.GameplayFacts.Processing;

/// <summary>Explicit processing composition boundary; legacy processing remains unchanged.</summary>
public sealed class GameplayFactProcessorRouter(NoCtfDbContext db, GameplayFactProcessor ordinary,
    ILiveSoloMatchStore scoped, TimeProvider clock) : IGameplayFactProcessor
{
    public async Task ProcessAsync(Guid factId, CancellationToken ct)
    {
        var round = await db.LiveSoloSubmissions.AsNoTracking().Where(x => x.GameplayFactId == factId).Select(x => (Guid?)x.RoundId).SingleOrDefaultAsync(ct);
        if (round is Guid roundId) await scoped.ResolveAsync(roundId, clock.GetUtcNow(), ct);
        else await ordinary.ProcessAsync(factId, ct);
    }
}

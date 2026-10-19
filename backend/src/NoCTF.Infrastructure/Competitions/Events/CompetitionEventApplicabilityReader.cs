using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Competitions.Modes;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Events;

public sealed class CompetitionEventApplicabilityReader(NoCtfDbContext db) : ICompetitionEventApplicabilityReader
{
    // A competition's mode is its TPH discriminator and never changes after creation,
    // so the memo can never become stale. Unknown competitions are not memoized.
    private static readonly ConcurrentDictionary<Guid, GameMode> Modes = new();

    public async Task<bool> HasOrdinaryScoreboardAsync(Guid competitionId, CancellationToken ct) =>
        await ModeAsync(competitionId, ct) is { } value && CompetitionModeCapabilities.For(value).OrdinaryScoreboard;

    public async Task<bool> CanPublishAsync(Guid competitionId, CompetitionEventKind kind, CancellationToken ct) =>
        await ModeAsync(competitionId, ct) is { } value && CompetitionEventApplicability.PubliclyApplicable(value, kind);

    private async ValueTask<GameMode?> ModeAsync(Guid competitionId, CancellationToken ct)
    {
        if (Modes.TryGetValue(competitionId, out var known))
            return known;
        var mode = await db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == competitionId)
            .Select(x => (GameMode?)x.Mode).SingleOrDefaultAsync(ct);
        if (mode is { } value)
            Modes.TryAdd(competitionId, value);
        return mode;
    }
}

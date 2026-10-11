using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Events;

public sealed class CompetitionEventApplicabilityReader(NoCtfDbContext db) : ICompetitionEventApplicabilityReader
{
    public async Task<bool> HasOrdinaryScoreboardAsync(Guid competitionId, CancellationToken ct)
    {
        var mode = await db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == competitionId)
            .Select(x => (NoCTF.Domain.Competitions.GameMode?)x.Mode).SingleOrDefaultAsync(ct);
        return mode is { } value && NoCTF.Application.Competitions.Modes.CompetitionModeCapabilities.For(value).OrdinaryScoreboard;
    }
    public async Task<bool> CanPublishAsync(Guid competitionId, CompetitionEventKind kind, CancellationToken ct)
    {
        var mode = await db.Competitions.IgnoreQueryFilters().AsNoTracking().Where(x => x.Id == competitionId)
            .Select(x => (NoCTF.Domain.Competitions.GameMode?)x.Mode).SingleOrDefaultAsync(ct);
        return mode is { } value && CompetitionEventApplicability.PubliclyApplicable(value, kind);
    }
}

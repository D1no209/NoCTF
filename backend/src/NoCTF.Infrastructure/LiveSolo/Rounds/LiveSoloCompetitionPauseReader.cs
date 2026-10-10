using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Rounds;

internal static class LiveSoloCompetitionPauseReader
{
    public static async Task<IReadOnlyDictionary<Guid, CompetitionLifecycleMoment[]>> ReadAsync(
        NoCtfDbContext db, IReadOnlyCollection<Guid> competitionIds, DateTimeOffset now, CancellationToken ct)
    {
        var transitions = await db.CompetitionEvents.AsNoTracking()
            .Where(x => competitionIds.Contains(x.CompetitionId)
                && x.Kind == CompetitionEventKind.CompetitionLifecycleChanged && x.OccurredAt <= now)
            .Select(x => new { x.CompetitionId, x.Id, x.OccurredAt, x.PreviousCompetitionStatus, x.CompetitionStatus })
            .ToArrayAsync(ct);
        return transitions.GroupBy(x => x.CompetitionId).ToDictionary(x => x.Key, x => x.Select(moment =>
            new CompetitionLifecycleMoment(moment.Id, moment.OccurredAt,
                moment.PreviousCompetitionStatus ?? throw new InvalidOperationException("Incomplete lifecycle event."),
                moment.CompetitionStatus ?? throw new InvalidOperationException("Incomplete lifecycle event."))).ToArray());
    }
}

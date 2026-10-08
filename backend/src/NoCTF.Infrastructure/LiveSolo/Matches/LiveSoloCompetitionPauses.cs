using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Lifecycle;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.LiveSolo;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private async Task SynchronizeCompetitionPausesAsync(LiveSoloRound round, Guid competitionId, DateTimeOffset now, bool persist, CancellationToken ct)
    {
        var transitions = await db.CompetitionEvents.AsNoTracking().Where(x => x.CompetitionId == competitionId
            && x.Kind == CompetitionEventKind.CompetitionLifecycleChanged && x.OccurredAt <= now)
            .Select(x => new { x.Id, x.OccurredAt, x.PreviousCompetitionStatus, x.CompetitionStatus }).ToArrayAsync(ct);
        var intervals = LiveSoloCompetitionPausePolicy.Project(round.Id, round.CreatedAt, now, transitions.Select(x =>
            new CompetitionLifecycleMoment(x.Id, x.OccurredAt, x.PreviousCompetitionStatus ?? throw new InvalidOperationException("Incomplete lifecycle event."),
                x.CompetitionStatus ?? throw new InvalidOperationException("Incomplete lifecycle event."))));
        if (!persist)
        {
            round.Pauses = round.Pauses.Where(x => x.Source != LiveSoloPauseSource.Competition).Concat(intervals).ToList();
            return;
        }
        var changed = false;
        foreach (var interval in intervals)
        {
            var existing = round.Pauses.SingleOrDefault(x => x.Id == interval.Id);
            if (existing is null)
            {
                round.Pauses.Add(interval); db.Set<LiveSoloPauseInterval>().Add(interval); changed = true;
            }
            else if (existing.EndedAt != interval.EndedAt) { existing.EndedAt = interval.EndedAt; changed = true; }
        }
        if (changed) { round.TimelineRevision++; round.ConcurrencyStamp = Guid.NewGuid(); }
    }
}

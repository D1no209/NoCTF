using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Infrastructure.LiveSolo.Rounds;

namespace NoCTF.Infrastructure.LiveSolo.Matches;

public sealed partial class LiveSoloMatchStore
{
    private async Task SynchronizeCompetitionPausesAsync(LiveSoloRound round, Guid competitionId, DateTimeOffset now, bool persist, CancellationToken ct)
    {
        var transitions = await LiveSoloCompetitionPauseReader.ReadAsync(db, [competitionId], now, ct);
        var intervals = LiveSoloCompetitionPausePolicy.Project(round.Id, round.CreatedAt, now,
            transitions.GetValueOrDefault(competitionId, []));
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

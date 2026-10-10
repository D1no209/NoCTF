using Microsoft.EntityFrameworkCore;
using NoCTF.Application.LiveSolo.Rounds;
using NoCTF.Domain.LiveSolo;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.LiveSolo.Rounds;

public sealed class LiveSoloScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rounds = await db.LiveSoloRounds.AsNoTracking().Include(x => x.Questions).Include(x => x.Pauses).Where(x =>
                x.State == LiveSoloRoundState.Preparing || x.State == LiveSoloRoundState.Countdown
                || x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult
                || x.Questions.Any(q => db.RuntimeInstances.Any(r => r.ExecutionScopeId == q.Id
                    && (r.State == RuntimeState.Queued || r.State == RuntimeState.Provisioning || r.State == RuntimeState.Running))))
            .ToArrayAsync(ct);
        var matchIds = rounds.Select(x => x.MatchId).Distinct().ToArray();
        var competitions = await db.LiveSoloMatches.AsNoTracking().Where(x => matchIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.CompetitionId, ct);
        var transitions = await LiveSoloCompetitionPauseReader.ReadAsync(db, competitions.Values.Distinct().ToArray(), now, ct);
        var result = new List<ClusterScheduleEntry>();
        foreach (var round in rounds)
        {
            // Lifecycle events are authoritative even when the last Tick persisted an open pause.
            // This read projection lets a replacement Worker schedule the Tick that closes it.
            round.Pauses = round.Pauses.Where(x => x.Source != LiveSoloPauseSource.Competition)
                .Concat(LiveSoloCompetitionPausePolicy.Project(round.Id, round.CreatedAt, now,
                    transitions.GetValueOrDefault(competitions[round.MatchId], []))).ToList();
            var due = round.State is LiveSoloRoundState.Won or LiveSoloRoundState.TimedOut or LiveSoloRoundState.Canceled
                ? now : LiveSoloRoundSchedulePolicy.NextWakeup(round, now);
            if (due is not { } at) continue;
            result.Add(new($"live-solo-round:{round.Id:N}:{round.TimelineRevision}", ClusterScheduleKind.LiveSoloRound, at, null,
                new AdvanceLiveSoloRound(round.Id, round.TimelineRevision, at)));
        }
        return result;
    }
}

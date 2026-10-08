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
        var rounds = await db.LiveSoloRounds.AsNoTracking().Where(x =>
                x.State == LiveSoloRoundState.Preparing || x.State == LiveSoloRoundState.Countdown
                || x.State == LiveSoloRoundState.Running || x.State == LiveSoloRoundState.ConfirmingResult
                || x.Questions.Any(q => db.RuntimeInstances.Any(r => r.ExecutionScopeId == q.Id
                    && (r.State == RuntimeState.Queued || r.State == RuntimeState.Provisioning || r.State == RuntimeState.Running))))
            .Select(x => new { x.Id, x.TimelineRevision }).ToArrayAsync(ct);
        return rounds.Select(x => new ClusterScheduleEntry($"live-solo-round:{x.Id:N}:{x.TimelineRevision}",
            ClusterScheduleKind.LiveSoloRound, now, TimeSpan.FromMilliseconds(500), new AdvanceLiveSoloRound(x.Id, x.TimelineRevision, now))).ToArray();
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Competitions.Lifecycle;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Competitions.Awdp;

public sealed class AwdpRoundScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var competitions = await db.Competitions.AsNoTracking().IgnoreAutoIncludes()
            .Where(competition => competition.Mode == GameMode.Awdp
                && competition.Status == CompetitionStatus.Running && competition.DeletedAt == null)
            .Select(competition => new
            {
                competition.Id, competition.StartAt, competition.EndAt,
                ((AwdpCompetitionModeConfiguration)competition.ModeConfiguration!).RoundDurationSeconds
            }).ToArrayAsync(ct);
        var entries = new List<ClusterScheduleEntry>();
        foreach (var competition in competitions)
        {
            var runtime = await CompetitionEffectiveRuntimeReader.ReadAsync(db, competition.Id,
                competition.StartAt, competition.EndAt, now, ct);
            if (runtime.RunningSince is null) continue;
            if (competition.RoundDurationSeconds <= 0)
                throw new InvalidOperationException("AWDP round duration must be positive.");
            var interval = TimeSpan.FromSeconds(competition.RoundDurationSeconds);
            var dueAt = now.AddTicks(interval.Ticks - runtime.Elapsed.Ticks % interval.Ticks);
            if (dueAt >= competition.EndAt) continue;
            entries.Add(new($"awdp-round:{competition.Id:N}", ClusterScheduleKind.AwdpRound,
                dueAt, interval, new ProjectLeaderboard(competition.Id)));
        }
        return entries;
    }
}

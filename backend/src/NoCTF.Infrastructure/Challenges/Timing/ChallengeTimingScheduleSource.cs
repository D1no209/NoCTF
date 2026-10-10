using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Timing;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Timing;

public sealed class ChallengeTimingScheduleSource(NoCtfDbContext db) : IClusterScheduleContributor
{
    public async Task<IReadOnlyList<ClusterScheduleEntry>> RebuildAsync(DateTimeOffset now, CancellationToken ct)
    {
        var rows = await db.CompetitionChallenges.AsNoTracking().Where(x => x.Mode != GameMode.LiveSolo
            && (x.OpeningState == ChallengeOpeningState.Pending || x.TimingRevision != x.AppliedTimingRevision))
            .Select(x => new { x.Id, x.TimingRevision, x.AppliedTimingRevision, x.OpeningState, x.AutoOpenAt }).ToArrayAsync(ct);
        return rows.SelectMany(x =>
        {
            var entries = new List<ClusterScheduleEntry>();
            if (x.OpeningState == ChallengeOpeningState.Pending && x.AutoOpenAt is { } opening)
                entries.Add(new($"challenge-opening:{x.Id:N}", ClusterScheduleKind.ChallengeTiming,
                    opening > now ? opening : now, TimeSpan.FromSeconds(5), new AdvanceChallengeOpening(x.Id, x.TimingRevision)));
            if (x.TimingRevision != x.AppliedTimingRevision)
                entries.Add(new($"challenge-timing:{x.Id:N}", ClusterScheduleKind.ChallengeTiming, now,
                    TimeSpan.FromSeconds(5), new RecalculateChallengeTiming(x.Id, x.TimingRevision)));
            return entries;
        }).ToArray();
    }
}

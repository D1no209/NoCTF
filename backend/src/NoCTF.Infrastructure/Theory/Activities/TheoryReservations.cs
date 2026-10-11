using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Activities;

internal static class TheoryReservations
{
    internal static async Task<HashSet<Guid>> ReadAsync(NoCtfDbContext db, Guid[] sources, Guid currentActivity,
        DateTimeOffset now, CancellationToken ct)
    {
        var reservations = await db.Set<TheoryDrawCandidate>().AsNoTracking().Where(candidate => sources.Contains(candidate.SourceId))
            .Join(db.Set<TheoryDrawRule>(), candidate => candidate.RuleId, rule => rule.Id, (candidate, rule) => new { candidate.SourceId, rule.ActivityId })
            .Join(db.TheoryActivities.Where(activity => activity.Id != currentActivity && activity.Kind == TheoryActivityKind.Competition
                && activity.State == TheoryActivityState.Published), candidate => candidate.ActivityId, activity => activity.Id,
                (candidate, activity) => new { candidate.SourceId, ActivityId = activity.Id }).Distinct().ToArrayAsync(ct);
        var active = new HashSet<Guid>();
        foreach (var id in reservations.Select(item => item.ActivityId).Distinct())
        {
            var access = await TheoryActivityAccess.ReadAsync(db, id, null, now, ct);
            if (access is not null && now < access.Deadline.At && access.Competition?.Status != CompetitionStatus.Finished) active.Add(id);
        }
        return reservations.Where(item => active.Contains(item.ActivityId)).Select(item => item.SourceId).ToHashSet();
    }
}

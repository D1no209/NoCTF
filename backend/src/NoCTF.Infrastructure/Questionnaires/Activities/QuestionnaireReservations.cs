using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires.Activities;

internal static class QuestionnaireReservations
{
    internal static async Task<HashSet<Guid>> ReadAsync(NoCtfDbContext db, Guid[] sources, Guid currentActivity,
        DateTimeOffset now, CancellationToken ct)
    {
        var reservations = await db.Set<QuestionnaireDrawCandidate>().AsNoTracking().Where(candidate => sources.Contains(candidate.SourceId))
            .Join(db.Set<QuestionnaireDrawRule>(), candidate => candidate.RuleId, rule => rule.Id, (candidate, rule) => new { candidate.SourceId, rule.ActivityId })
            .Join(db.QuestionnaireActivities.Where(activity => activity.Id != currentActivity && activity.Kind == QuestionnaireActivityKind.Examination
                && activity.State == QuestionnaireActivityState.Published), candidate => candidate.ActivityId, activity => activity.Id,
                (candidate, activity) => new { candidate.SourceId, ActivityId = activity.Id }).Distinct().ToArrayAsync(ct);
        var active = new HashSet<Guid>();
        foreach (var id in reservations.Select(item => item.ActivityId).Distinct())
        {
            var access = await QuestionnaireActivityAccess.ReadAsync(db, id, null, now, ct);
            if (access is not null && now < access.Deadline.At) active.Add(id);
        }
        return reservations.Where(item => active.Contains(item.ActivityId)).Select(item => item.SourceId).ToHashSet();
    }
}

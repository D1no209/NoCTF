using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Questionnaires.Activities;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Questionnaires.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Questionnaires.Activities;

internal sealed record QuestionnaireAccess(QuestionnaireActivity Activity, bool CanManage, bool CanJudge, bool CanObserve,
    bool CanParticipate, IReadOnlyList<QuestionnairePauseInterval> Pauses, QuestionnaireDeadline Deadline);

internal static class QuestionnaireActivityAccess
{
    internal static async Task<QuestionnaireAccess?> ReadAsync(NoCtfDbContext db, Guid activityId, Guid? actorId,
        DateTimeOffset now, CancellationToken ct, bool includeCandidates = false)
    {
        var user = actorId is null ? null : await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(value => value.Id == actorId && value.AccountStatus == UserAccountStatus.Active, ct);
        if (actorId is not null && user is null) return null;
        IQueryable<QuestionnaireActivity> activities = db.QuestionnaireActivities.Include(activity => activity.Rules).ThenInclude(rule => rule.Filters)
            .Include(activity => activity.Pauses);
        if (includeCandidates) activities = activities.Include(activity => activity.Rules).ThenInclude(rule => rule.Candidates);
        if (db.Database.CurrentTransaction is not null) activities = activities.AsSplitQuery();
        var activity = await activities.SingleOrDefaultAsync(value => value.Id == activityId, ct);
        if (activity is null) return null;
        var manage = user?.Role == UserRole.Administrator || activity.OwnerId == actorId;
        var participate = user?.Kind == UserKind.Human;
        if (actorId is not null && !manage && (!participate || activity.State == QuestionnaireActivityState.Draft)) return null;
        var pauses = activity.Pauses.Select(pause => new QuestionnairePauseInterval(pause.StartedAt, pause.EndedAt)).ToArray();
        var deadline = QuestionnaireClock.Calculate(activity.PublishedAt ?? activity.OpensAt, activity.ClosesAt, pauses, now, activity.FinishedAt);
        return new(activity, manage, manage, manage, participate, pauses, deadline);
    }
}

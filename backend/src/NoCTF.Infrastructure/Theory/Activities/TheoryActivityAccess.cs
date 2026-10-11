using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Activities;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Domain.Theory.Activities;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Activities;

internal sealed record TheoryAccess(TheoryActivity Activity, Competition? Competition, Guid? TeamId,
    bool CanManage, bool CanJudge, bool CanObserve, bool CanParticipate,
    IReadOnlyList<TheoryPauseInterval> Pauses, TheoryDeadline Deadline);

internal static class TheoryActivityAccess
{
    internal static async Task<TheoryAccess?> ReadAsync(NoCtfDbContext db, Guid activityId, Guid? actorId,
        DateTimeOffset now, CancellationToken ct, bool includeCandidates = false)
    {
        var user = actorId is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actorId && user.AccountStatus == UserAccountStatus.Active, ct);
        if (actorId is not null && user is null) return null;
        IQueryable<TheoryActivity> activities = db.TheoryActivities.Include(activity => activity.Rules).ThenInclude(rule => rule.Filters)
            .Include(activity => activity.Pauses);
        if (includeCandidates) activities = activities.Include(activity => activity.Rules).ThenInclude(rule => rule.Candidates);
        if (db.Database.CurrentTransaction is not null) activities = activities.AsSplitQuery();
        var activity = await activities.SingleOrDefaultAsync(activity => activity.Id == activityId, ct);
        if (activity is null) return null;
        var link = await db.CompetitionTheoryActivities.AsNoTracking().SingleOrDefaultAsync(link => link.ActivityId == activityId, ct);
        var competition = link is null ? null : await db.Competitions.AsNoTracking().Include(competition => competition.Collaborators)
            .SingleOrDefaultAsync(competition => competition.Id == link.CompetitionId && competition.DeletedAt == null, ct);
        if (activity.Kind == TheoryActivityKind.Competition && (competition is null || !TheoryActivityPolicy.Supports(competition.Mode))) return null;
        var manage = user?.Role == UserRole.Administrator || (competition is null ? activity.OwnerId == actorId
            : competition.OwnerId == actorId || competition.Collaborators.Any(member => member.UserId == actorId && member.Role == CompetitionCollaboratorRole.Manager));
        var judge = manage || competition?.Collaborators.Any(member => member.UserId == actorId && member.Role == CompetitionCollaboratorRole.Judge) == true;
        var observe = judge || competition?.Collaborators.Any(member => member.UserId == actorId && member.Role == CompetitionCollaboratorRole.Observer) == true;
        var teamId = competition is null ? null : await db.Teams.AsNoTracking().Where(team => team.CompetitionId == competition.Id
                && team.DeletedAt == null && !team.IsBanned && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.Members.Any(member => member.UserId == actorId && member.ActiveMembership != null))
            .Select(team => (Guid?)team.Id).SingleOrDefaultAsync(ct);
        var participate = user?.Kind == UserKind.Human && (competition is null
            ? activity.Kind != TheoryActivityKind.Competition : link!.Enabled && teamId is not null);
        if (actorId is not null && !observe && (!participate || activity.State == TheoryActivityState.Draft)) return null;
        var pauses = activity.Pauses.Select(pause => new TheoryPauseInterval(pause.StartedAt, pause.EndedAt)).ToList();
        DateTimeOffset? hardEnd = competition?.EndAt;
        if (competition is not null)
        {
            var transitions = await db.CompetitionEvents.AsNoTracking().Where(item => item.CompetitionId == competition.Id
                    && item.Kind == CompetitionEventKind.CompetitionLifecycleChanged)
                .OrderBy(item => item.OccurredAt).ThenBy(item => item.Id)
                .Select(item => new { item.OccurredAt, item.CompetitionStatus }).ToArrayAsync(ct);
            DateTimeOffset? pauseStart = null;
            foreach (var transition in transitions)
            {
                if (transition.CompetitionStatus == CompetitionStatus.Paused) pauseStart ??= transition.OccurredAt;
                else if (pauseStart is { } start) { pauses.Add(new(start, transition.OccurredAt)); pauseStart = null; }
                if (transition.CompetitionStatus == CompetitionStatus.Finished && transition.OccurredAt < hardEnd) hardEnd = transition.OccurredAt;
            }
            if (pauseStart is { } activePause) pauses.Add(new(activePause, null));
        }
        if (activity.FinishedAt is { } finished && (hardEnd is null || finished < hardEnd)) hardEnd = finished;
        var deadline = TheoryClock.Calculate(activity.PublishedAt ?? activity.OpensAt, activity.ClosesAt, pauses, now, hardEnd);
        return new(activity, competition, teamId, manage, judge, observe, participate, pauses, deadline);
    }
}

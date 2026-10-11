using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Theory.Realtime;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Theory.Realtime;

public sealed class TheoryChangeAudienceResolver(IDbContextFactory<NoCtfDbContext> contexts)
{
    public async Task<HashSet<Guid>> ResolveAsync(TheoryChanged change, CancellationToken ct)
    {
        await using var db = await contexts.CreateDbContextAsync(ct);
        var activity = await db.TheoryActivities.AsNoTracking().SingleOrDefaultAsync(item => item.Id == change.ActivityId, ct);
        if (activity is null) return [];
        var competitionId = await db.CompetitionTheoryActivities.Where(link => link.ActivityId == change.ActivityId).Select(link => (Guid?)link.CompetitionId).SingleOrDefaultAsync(ct);
        var users = new HashSet<Guid>();
        if (competitionId is null) users.Add(activity.OwnerId);
        if (competitionId is { } competition)
        {
            if (!await db.Competitions.AnyAsync(item => item.Id == competition && item.DeletedAt == null, ct)) return [];
            var owner = await db.Competitions.Where(item => item.Id == competition).Select(item => item.OwnerId).SingleAsync(ct);
            users.Add(owner);
            users.UnionWith(await db.Competitions.Where(item => item.Id == competition).SelectMany(item => item.Collaborators).Select(item => item.UserId).ToArrayAsync(ct));
        }
        var participants = db.TheoryParticipants.AsNoTracking().Where(item => item.ActivityId == change.ActivityId
            && (change.ParticipantId == null || item.Id == change.ParticipantId));
        users.UnionWith(await participants.Where(item => item.UserId != null).Select(item => item.UserId!.Value).ToArrayAsync(ct));
        var teams = competitionId is not null && change.ParticipantId is null
            ? db.Teams.Where(team => team.CompetitionId == competitionId).Select(team => team.Id)
            : participants.Where(item => item.TeamId != null).Select(item => item.TeamId!.Value);
        users.UnionWith(await db.Teams.Where(team => teams.Contains(team.Id) && team.DeletedAt == null && !team.IsBanned
            && team.RegistrationStatus == TeamRegistrationStatus.Approved).SelectMany(team => team.Members)
            .Where(member => member.ActiveMembership != null).Select(member => member.UserId).ToArrayAsync(ct));
        return (await db.Users.Where(user => user.AccountStatus == UserAccountStatus.Active
            && (users.Contains(user.Id) || user.Role == UserRole.Administrator)).Select(user => user.Id).ToArrayAsync(ct)).ToHashSet();
    }
}

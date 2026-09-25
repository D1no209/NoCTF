using Microsoft.EntityFrameworkCore;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Notifications;

public sealed class CompetitionNotificationAudienceResolver(NoCtfDbContext db)
{
    public async Task<IReadOnlyList<Guid>> ResolveAsync(
        Guid competitionId,
        Guid? requiredTeamId,
        CancellationToken ct)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate =>
                candidate.Id == competitionId
                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.OwnerId,
                ManagerIds = candidate.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Manager)
                    .Select(item => item.UserId).ToArray(),
                JudgeIds = candidate.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Judge)
                    .Select(item => item.UserId).ToArray(),
                ObserverIds = candidate.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Observer)
                    .Select(item => item.UserId).ToArray(),
                candidate.AccessMode
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return [];

        var teamMembers = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team =>
                team.CompetitionId == competitionId
                && team.DeletedAt == null
                && (team.RegistrationStatus == TeamRegistrationStatus.Approved
                    && !team.IsBanned
                    || requiredTeamId != null && team.Id == requiredTeamId))
            .Select(team => new
            {
                team.CaptainId,
                MemberIds = team.Members.Select(member => member.UserId).ToArray()
            })
            .ToArrayAsync(ct);
        var collaborators = competition.ManagerIds
            .Concat(competition.JudgeIds)
            .Concat(competition.ObserverIds)
            .Append(competition.OwnerId)
            .Distinct();
        var candidateIds = (competition.AccessMode == CompetitionAccessMode.StaffOnly
                ? collaborators
                : collaborators.Concat(
                    teamMembers.SelectMany(team => team.MemberIds.Append(team.CaptainId))))
            .Distinct()
            .ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => candidateIds.Contains(user.Id))
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }
}

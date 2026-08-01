using Microsoft.EntityFrameworkCore;
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
                candidate.ManagerIds,
                candidate.JudgeIds,
                candidate.ObserverIds
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
            .Select(team => new { team.CaptainId, team.MemberIds })
            .ToArrayAsync(ct);
        var candidateIds = competition.ManagerIds
            .Concat(competition.JudgeIds)
            .Concat(competition.ObserverIds)
            .Append(competition.OwnerId)
            .Concat(teamMembers.SelectMany(team => team.MemberIds.Append(team.CaptainId)))
            .Distinct()
            .ToArray();
        return await db.Users.AsNoTracking()
            .Where(user => candidateIds.Contains(user.Id))
            .OrderBy(user => user.Id)
            .Select(user => user.Id)
            .ToArrayAsync(ct);
    }
}

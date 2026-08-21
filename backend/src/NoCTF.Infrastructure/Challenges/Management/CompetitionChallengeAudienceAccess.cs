using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Teams;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class CompetitionChallengeAudienceAccess(NoCtfDbContext db)
    : ICompetitionChallengeAudienceAccess
{
    public async Task<bool> CanReadAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
            return false;

        var identity = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new { user.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
            return false;
        if (identity.Role == UserRole.Administrator)
            return true;

        var collaborator = await db.Competitions.AsNoTracking().AnyAsync(
            competition => competition.Id == competitionId
                && competition.DeletedAt == null
                && (competition.OwnerId == userId
                    || competition.ManagerIds.Contains(userId)
                    || competition.JudgeIds.Contains(userId)
                    || competition.ObserverIds.Contains(userId)),
            cancellationToken);
        if (collaborator)
            return true;

        return await db.Teams.AsNoTracking().AnyAsync(
            team => team.CompetitionId == competitionId
                && team.DeletedAt == null
                && !team.IsBanned
                && team.RegistrationStatus == TeamRegistrationStatus.Approved
                && team.MemberIds.Contains(userId),
            cancellationToken);
    }
}

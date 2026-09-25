using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionHubAccess(NoCtfDbContext db) : ICompetitionHubAccess
{
    public async Task<CompetitionHubAccessDecision?> ResolveAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var competition = await db.Competitions.AsNoTracking()
            .Where(candidate => candidate.Id == competitionId
                && candidate.DeletedAt == null)
            .Select(candidate => new
            {
                candidate.Status,
                candidate.AccessMode,
                candidate.OwnerId,
                IsCollaborator = candidate.Collaborators.Any(collaborator =>
                    collaborator.UserId == userId)
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (competition is null)
            return null;

        var identity = await db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new { user.Role })
            .SingleOrDefaultAsync(cancellationToken);
        if (identity is null)
            return null;

        var isStaff = identity.Role == UserRole.Administrator
            || competition.OwnerId == userId
            || competition.IsCollaborator;
        if ((competition.Status == CompetitionStatus.Draft
                || competition.AccessMode == CompetitionAccessMode.StaffOnly)
            && !isStaff)
            return null;

        return new(isStaff);
    }
}

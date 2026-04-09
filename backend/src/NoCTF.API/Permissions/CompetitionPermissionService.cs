using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Permissions;

public interface ICompetitionPermissionService
{
    Task<bool> CanManageCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default);
    Task<bool> CanViewCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default);
}

public class CompetitionPermissionService(ApplicationDbContext dbContext) : ICompetitionPermissionService
{
    public async Task<bool> CanManageCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FindAsync(new object[] { userId }, cancellationToken);
        if (user?.Role == UserRole.Admin || user?.Role == UserRole.Organizer)
            return true;

        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == competitionId, cancellationToken);
        if (competition?.OwnerId == userId)
            return true;

        var collaborator = await dbContext.CompetitionCollaborators
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.CompetitionId == competitionId && c.UserId == userId, cancellationToken);

        return collaborator?.Role == CollaboratorRole.Manager;
    }

    public async Task<bool> CanViewCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default)
    {
        var competition = await dbContext.Competitions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == competitionId, cancellationToken);
        if (competition is null) return false;
        if (competition.Status == CompetitionStatus.Published || competition.Status == CompetitionStatus.Running)
            return true;

        return await CanManageCompetitionAsync(userId, competitionId, cancellationToken);
    }
}

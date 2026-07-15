using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Permissions;

public interface ICompetitionPermissionService
{
    Task<bool> CanManageCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default);
    Task<bool> CanViewCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default);
    Task<HashSet<Guid>> GetManageableCompetitionIdsAsync(Guid userId, CancellationToken cancellationToken = default);
}

public class CompetitionPermissionService(ApplicationDbContext dbContext) : ICompetitionPermissionService
{
    public async Task<bool> CanManageCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default)
    {
        var admin = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Role == UserRole.Admin)
            .Select(_ => 1);

        var owned = dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition => competition.Id == competitionId && competition.OwnerId == userId)
            .Select(_ => 1);

        var managed = dbContext.CompetitionCollaborators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(collaborator =>
                collaborator.CompetitionId == competitionId &&
                collaborator.UserId == userId &&
                collaborator.Role == CollaboratorRole.Manager)
            .Select(_ => 1);

        return await admin
            .Concat(owned)
            .Concat(managed)
            .AnyAsync(cancellationToken);
    }

    public async Task<bool> CanViewCompetitionAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken = default)
    {
        var userAccess = dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId && user.Role == UserRole.Admin)
            .Select(_ => 1);
        var competitionAccess = dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                (competition.OwnerId == userId ||
                 competition.Status == CompetitionStatus.Published ||
                 competition.Status == CompetitionStatus.Running ||
                 competition.Status == CompetitionStatus.Paused ||
                 competition.Status == CompetitionStatus.Finished))
            .Select(_ => 1);
        var collaboratorAccess = dbContext.CompetitionCollaborators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(collaborator =>
                collaborator.CompetitionId == competitionId &&
                collaborator.UserId == userId &&
                collaborator.Role == CollaboratorRole.Manager)
            .Select(_ => 1);

        return await userAccess
            .Concat(competitionAccess)
            .Concat(collaboratorAccess)
            .AnyAsync(cancellationToken);
    }

    public async Task<HashSet<Guid>> GetManageableCompetitionIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var ownedOrAdministratorIds = dbContext.Competitions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition =>
                competition.OwnerId == userId ||
                dbContext.Users.AsNoTracking().Any(user =>
                    user.Id == userId && user.Role == UserRole.Admin))
            .Select(c => c.Id);

        var managedIds = dbContext.CompetitionCollaborators
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.Role == CollaboratorRole.Manager)
            .Select(c => c.CompetitionId);

        return (await ownedOrAdministratorIds
                .Concat(managedIds)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Permissions;

public interface ITeamPermissionService
{
    Task<bool> IsCaptainAsync(Guid userId, Guid teamId, CancellationToken cancellationToken = default);
    Task<bool> IsTeamMemberAsync(Guid userId, Guid teamId, CancellationToken cancellationToken = default);
}

public class TeamPermissionService(ApplicationDbContext dbContext) : ITeamPermissionService
{
    public async Task<bool> IsCaptainAsync(Guid userId, Guid teamId, CancellationToken cancellationToken = default)
    {
        var member = await dbContext.TeamMembers
            .FirstOrDefaultAsync(tm => tm.TeamId == teamId && tm.UserId == userId, cancellationToken);
        return member?.Role == TeamMemberRole.Captain;
    }

    public async Task<bool> IsTeamMemberAsync(Guid userId, Guid teamId, CancellationToken cancellationToken = default)
    {
        return await dbContext.TeamMembers
            .AnyAsync(tm => tm.TeamId == teamId && tm.UserId == userId, cancellationToken);
    }
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Membership;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Teams.Membership;

public sealed class AdminTeamInvitationReader(NoCtfDbContext db)
    : IAdminTeamInvitationReader
{
    public Task<string?> ReadAsync(
        Guid competitionId,
        Guid teamId,
        CancellationToken cancellationToken) =>
        db.Teams.AsNoTracking()
            .Where(team => team.CompetitionId == competitionId && team.Id == teamId)
            .Select(team => team.InvitationToken)
            .SingleOrDefaultAsync(cancellationToken);
}

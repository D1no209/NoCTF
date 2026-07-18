using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionModerationAuthorizer(NoCtfDbContext db) : ICompetitionModerationAuthorizer
{
    public async Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && (user.Role == UserRole.Administrator || user.Role == UserRole.Organizer),
            cancellationToken);
        return privileged || await db.CompetitionCollaborators.AsNoTracking().AnyAsync(collaborator =>
            collaborator.CompetitionId == competitionId && collaborator.UserId == userId,
            cancellationToken);
    }
}

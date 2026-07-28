using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionHubAccess(NoCtfDbContext db) : ICompetitionHubAccess
{
    public Task<bool> CanJoinAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId
            && competition.DeletedAt == null
            && competition.Status != CompetitionStatus.Draft,
            cancellationToken);
}

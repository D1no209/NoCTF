using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Persistence.UseCaseAdapters;

public sealed class EfCompetitionHubAccess(NoCtfDbContext db) : ICompetitionHubAccess
{
    public Task<bool> CanJoinAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId
            && !competition.Deletion.IsDeleted
            && competition.Status != CompetitionStatus.Draft,
            cancellationToken);
}

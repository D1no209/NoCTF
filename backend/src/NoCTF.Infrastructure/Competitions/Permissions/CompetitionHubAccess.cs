using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Notifications;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionHubAccess(NoCtfDbContext db) : ICompetitionHubAccess
{
    public Task<bool> CanJoinAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken) =>
        db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId
            && competition.DeletedAt == null
            && (competition.Status != CompetitionStatus.Draft
                || db.Users.Any(user =>
                    user.Id == userId
                    && user.AccountStatus == UserAccountStatus.Active
                    && (user.Role == UserRole.Administrator
                        || competition.OwnerId == userId
                        || competition.ManagerIds.Contains(userId)
                        || competition.JudgeIds.Contains(userId)
                        || competition.ObserverIds.Contains(userId)))),
            cancellationToken);
}

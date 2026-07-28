using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Teams.Moderation;

public sealed class CompetitionModerationAuthorizer(NoCtfDbContext db) : ICompetitionModerationAuthorizer
{
    public async Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Administrator,
            cancellationToken);
        return privileged || await db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId && competition.DeletedAt == null
                && (competition.OwnerId == userId || competition.ManagerIds.Contains(userId)),
            cancellationToken);
    }

    public async Task<bool> CanJudgeAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Administrator, cancellationToken);
        return privileged
            || await db.Competitions.AsNoTracking().AnyAsync(competition =>
                competition.Id == competitionId && competition.DeletedAt == null
                    && (competition.OwnerId == userId
                        || competition.ManagerIds.Contains(userId)
                        || competition.JudgeIds.Contains(userId)),
                cancellationToken);
    }

    public async Task<bool> CanObserveAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Administrator, cancellationToken);
        return privileged
            || await db.Competitions.AsNoTracking().AnyAsync(competition =>
                competition.Id == competitionId && competition.DeletedAt == null
                    && (competition.OwnerId == userId
                        || competition.ManagerIds.Contains(userId)
                        || competition.JudgeIds.Contains(userId)
                        || competition.ObserverIds.Contains(userId)),
                cancellationToken);
    }
}

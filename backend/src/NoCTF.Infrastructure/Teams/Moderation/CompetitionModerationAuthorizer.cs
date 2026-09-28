using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Teams.Moderation;
using NoCTF.Domain.Identity;

namespace NoCTF.Infrastructure.Teams.Moderation;

public sealed class CompetitionModerationAuthorizer(NoCtfDbContext db) : ICompetitionModerationAuthorizer
{
    public async Task<bool> CanReadInternalHistoricalAuditAsync(Guid userId, Guid competitionId, CancellationToken ct) =>
        await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId && user.Role == UserRole.Administrator, ct)
        || await db.Competitions.IgnoreQueryFilters().AsNoTracking().AnyAsync(competition => competition.Id == competitionId
            && (competition.OwnerId == userId || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId) || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)), ct);
    public async Task<bool> CanModerateAsync(Guid userId, Guid competitionId, CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Administrator,
            cancellationToken);
        return privileged || await db.Competitions.AsNoTracking().AnyAsync(competition =>
            competition.Id == competitionId && competition.DeletedAt == null
                && (competition.OwnerId == userId || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)),
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
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)),
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
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer && collaborator.UserId == userId)),
                cancellationToken);
    }

    public async Task<bool> CanReadHistoricalAuditAsync(
        Guid userId,
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        var privileged = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId && user.Role == UserRole.Administrator, cancellationToken);
        return privileged
            || await db.Competitions.IgnoreQueryFilters().AsNoTracking().AnyAsync(competition =>
                competition.Id == competitionId
                    && (competition.OwnerId == userId
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Judge && collaborator.UserId == userId)
                        || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Observer && collaborator.UserId == userId)),
                cancellationToken);
    }
}

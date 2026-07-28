using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(NoCtfDbContext db) : IAdminCompetitionStore
{
    public async Task<IReadOnlyList<CompetitionView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct) =>
        await Authorized(db.Competitions.AsNoTracking(), actorId, isAdministrator)
            .OrderByDescending(competition => competition.StartAt)
            .ThenBy(competition => competition.Id)
            .Select(competition => new CompetitionView(
                competition.Id, competition.Title, competition.Description, competition.Mode,
                competition.StartAt, competition.EndAt, competition.Status,
                competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers, competition.OwnerId))
            .ToListAsync(ct);

    public Task<CompetitionView?> FindAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = includeDeleted
            ? db.Competitions.IgnoreQueryFilters().AsNoTracking()
            : db.Competitions.AsNoTracking();
        return Authorized(source, actorId, isAdministrator)
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new CompetitionView(
                competition.Id, competition.Title, competition.Description, competition.Mode,
                competition.StartAt, competition.EndAt, competition.Status,
                competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers, competition.OwnerId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<bool> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entity = await Authorized(
                db.Competitions.IgnoreQueryFilters(),
                actorId,
                isAdministrator)
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId && competition.DeletedAt != null, ct);
        if (entity is null)
            return false;
        entity.DeletedAt = null;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var entity = await Authorized(
                db.Competitions.IgnoreQueryFilters(),
                actorId,
                isAdministrator)
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId && competition.DeletedAt != null, ct);
        if (entity is null)
            return false;
        var hasDependents =
            await db.Teams.IgnoreQueryFilters().AnyAsync(team => team.CompetitionId == competitionId, ct) ||
            await db.CompetitionChallenges.IgnoreQueryFilters().AnyAsync(
                challenge => challenge.CompetitionId == competitionId, ct) ||
            await db.Submissions.IgnoreQueryFilters().AnyAsync(
                submission => submission.CompetitionId == competitionId, ct) ||
            await db.RuntimeInstances.AnyAsync(runtime => runtime.CompetitionId == competitionId, ct);
        if (hasDependents)
            return false;
        db.Competitions.Remove(entity);
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<CompetitionView?> TransferOwnerAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entity = await db.Competitions.SingleOrDefaultAsync(competition =>
            competition.Id == competitionId &&
            (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null || !await db.Users.AnyAsync(user => user.Id == ownerId, ct))
            return null;
        entity.OwnerId = ownerId;
        entity.ManagerIds = entity.ManagerIds.Where(id => id != ownerId).ToArray();
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Map(entity);
    }

    private static IQueryable<Competition> Authorized(
        IQueryable<Competition> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(competition =>
                competition.OwnerId == actorId ||
                competition.ManagerIds.Contains(actorId) ||
                competition.JudgeIds.Contains(actorId) ||
                competition.ObserverIds.Contains(actorId));

    private static CompetitionView Map(Competition competition) =>
        new(
            competition.Id, competition.Title, competition.Description, competition.Mode,
            competition.StartAt, competition.EndAt, competition.Status,
            competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers, competition.OwnerId);

}

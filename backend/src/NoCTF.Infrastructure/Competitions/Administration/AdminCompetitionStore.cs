using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Administration;

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(
    NoCtfDbContext db,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null)
    : IAdminCompetitionStore
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
                competition.TeamRegistrationAutoApprove,
                competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
                competition.AllowTeamRegistrationWhileRunning))
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
                competition.TeamRegistrationAutoApprove,
                competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
                competition.AllowTeamRegistrationWhileRunning))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CompetitionRestoreResult> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt != null &&
                (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return new(CompetitionRestoreState.NotFound);
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            entity.ManagerIds.Append(entity.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionRestoreState.UserNotFound,
                eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionRestoreState.RoleNotEligible,
                eligibility.RoleIneligibleUserIds);
        }
        entity.DeletedAt = null;
        entity.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CompetitionRestoreState.NotFound);
        }
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(entity.Id, ct);
        return new(CompetitionRestoreState.Restored);
    }

    public async Task<bool> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt != null &&
                (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return false;
        var hasDependents =
            await db.Teams.IgnoreQueryFilters().AnyAsync(team => team.CompetitionId == competitionId, ct) ||
            await db.CompetitionChallenges.IgnoreQueryFilters().AnyAsync(
                challenge => challenge.CompetitionId == competitionId, ct) ||
            await db.Submissions.IgnoreQueryFilters().AnyAsync(
                submission => submission.CompetitionId == competitionId, ct) ||
            await db.RuntimeInstances.AnyAsync(runtime => runtime.CompetitionId == competitionId, ct) ||
            await db.CompetitionEvents.AnyAsync(item => item.CompetitionId == competitionId, ct);
        if (hasDependents)
            return false;
        db.Entry(entity).State = EntityState.Detached;
        var deleted = await db.Competitions
            .IgnoreQueryFilters()
            .Where(competition =>
                competition.Id == competitionId
                && competition.DeletedAt != null
                && (isAdministrator || competition.OwnerId == actorId))
            .ExecuteDeleteAsync(ct);
        if (deleted == 0)
            return false;
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(competitionId, ct);
        return true;
    }

    public async Task<CompetitionOwnerTransferResult> TransferOwnerAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return new(CompetitionOwnerTransferState.NotFound);
        var entity = await db.Competitions.SingleOrDefaultAsync(competition =>
            competition.Id == competitionId &&
            (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return new(CompetitionOwnerTransferState.NotFound);
        var previousOwnerId = entity.OwnerId;
        var managerIds = entity.ManagerIds
            .Append(previousOwnerId)
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            managerIds.Append(ownerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionOwnerTransferState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionOwnerTransferState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }
        entity.OwnerId = ownerId;
        entity.ManagerIds = managerIds;
        entity.JudgeIds = entity.JudgeIds
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        entity.ObserverIds = entity.ObserverIds
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        entity.PermissionRevision = checked(entity.PermissionRevision + 1);
        entity.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CompetitionOwnerTransferState.RevisionConflict);
        }
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(entity.Id, ct);
        return new(CompetitionOwnerTransferState.Transferred, Map(entity));
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
            competition.TeamRegistrationAutoApprove,
            competition.MaxTeamMembers,
            competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
            competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
            competition.AllowTeamRegistrationWhileRunning);

}

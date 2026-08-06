using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionPermissionStore(NoCtfDbContext db) : ICompetitionPermissionStore
{
    public async Task<CompetitionPermissionSnapshotResult> GetSnapshotAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var snapshot = await db.Competitions.AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt == null)
            .Select(competition => new CompetitionPermissionSnapshot(
                competition.Id,
                competition.OwnerId,
                competition.ManagerIds,
                competition.JudgeIds,
                competition.ObserverIds,
                competition.PermissionRevision))
            .SingleOrDefaultAsync(ct);
        if (snapshot is null)
            return new(CompetitionPermissionSnapshotState.NotFound);
        if (!isAdministrator && snapshot.OwnerId != actorId)
            return new(CompetitionPermissionSnapshotState.Forbidden);

        return new(
            CompetitionPermissionSnapshotState.Found,
            snapshot with
            {
                ManagerIds = snapshot.ManagerIds.Distinct().Order().ToArray(),
                JudgeIds = snapshot.JudgeIds.Distinct().Order().ToArray(),
                ObserverIds = snapshot.ObserverIds.Distinct().Order().ToArray()
            });
    }

    public async Task<CompetitionPermissionCandidateListResult> ListCandidatesAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var ownerId = await db.Competitions.AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt == null)
            .Select(competition => (Guid?)competition.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (ownerId is null)
            return new(CompetitionPermissionCandidateListState.NotFound);
        if (!isAdministrator && ownerId.Value != actorId)
            return new(CompetitionPermissionCandidateListState.Forbidden);

        var candidates = await db.Users.AsNoTracking()
            .Where(user =>
                user.Id != ownerId.Value &&
                (user.Kind == UserKind.Bot ||
                 user.Role == UserRole.Organizer ||
                 user.Role == UserRole.Administrator ||
                 user.EmailVerifiedAt != null))
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Select(user => new CompetitionPermissionCandidate(
                user.Id,
                user.UserName,
                user.Kind,
                user.Role,
                user.EmailVerifiedAt != null))
            .ToArrayAsync(ct);
        return new(
            CompetitionPermissionCandidateListState.Listed,
            candidates);
    }

    public async Task<CompetitionPermissionUpdateResult> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompetitionWriteLock.AcquireAsync(db, command.CompetitionId, ct);
        var competition = await db.Competitions.SingleOrDefaultAsync(
            item => item.Id == command.CompetitionId && item.DeletedAt == null,
            ct);
        if (competition is null)
            return new(CompetitionPermissionUpdateState.NotFound);

        var administrator = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == command.ActorId && user.Role == UserRole.Administrator,
            ct);
        if (competition.OwnerId != command.ActorId && !administrator)
            return new(CompetitionPermissionUpdateState.Forbidden);
        if (competition.PermissionRevision != command.ExpectedPermissionRevision)
            return new(CompetitionPermissionUpdateState.RevisionConflict);

        var userIds = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .ToArray();
        if (userIds.Length != userIds.Distinct().Count())
            return new(CompetitionPermissionUpdateState.RolesOverlap);
        if (userIds.Contains(competition.OwnerId))
        {
            return new(
                CompetitionPermissionUpdateState.OwnerIncluded,
                [competition.OwnerId]);
        }

        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            command.ManagerIds.Append(competition.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.RoleNotEligible,
                eligibility.RoleIneligibleUserIds);
        }

        var judgeObserverIds = command.JudgeIds
            .Concat(command.ObserverIds)
            .Distinct()
            .Order()
            .ToArray();
        var judgeObserverUsers = await db.Users.AsNoTracking()
            .Where(user => judgeObserverIds.Contains(user.Id))
            .Select(user => new { user.Id, user.Kind, user.EmailVerifiedAt })
            .ToArrayAsync(ct);
        var missingUserIds = judgeObserverIds
            .Except(judgeObserverUsers.Select(user => user.Id))
            .ToArray();
        if (missingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                missingUserIds);
        }
        var ineligibleJudgeIds = judgeObserverUsers
            .Where(user => user.Kind == UserKind.Bot && command.JudgeIds.Contains(user.Id))
            .Select(user => user.Id)
            .Order()
            .ToArray();
        if (ineligibleJudgeIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.RoleNotEligible,
                ineligibleJudgeIds);
        }
        var unverifiedUserIds = judgeObserverUsers
            .Where(user =>
                user.Kind == UserKind.Human
                && user.EmailVerifiedAt is null)
            .Select(user => user.Id)
            .Order()
            .ToArray();
        if (unverifiedUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.EmailNotVerified,
                unverifiedUserIds);
        }

        competition.ManagerIds = command.ManagerIds.Distinct().Order().ToArray();
        competition.JudgeIds = command.JudgeIds.Distinct().Order().ToArray();
        competition.ObserverIds = command.ObserverIds.Distinct().Order().ToArray();
        competition.PermissionRevision = checked(competition.PermissionRevision + 1);
        competition.UpdatedAt = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CompetitionPermissionUpdateState.RevisionConflict);
        }
        await transaction.CommitAsync(ct);
        return new(CompetitionPermissionUpdateState.Updated);
    }
}

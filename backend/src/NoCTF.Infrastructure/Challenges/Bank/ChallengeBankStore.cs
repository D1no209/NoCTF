using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Challenges;
using Npgsql;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(NoCtfDbContext db) : IChallengeBankStore
{
    public async Task<ChallengeTemplateWriteResult> CreateAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        var challengeId = command.ChallengeId ?? Guid.CreateVersion7(command.CreatedAt);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            [command.OwnerId],
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }

        var entity = new Challenge
        {
            Id = challengeId,
            OwnerId = command.OwnerId,
            Mode = command.Mode,
            Visibility = command.Visibility,
            Title = command.Title,
            Description = command.Description,
            Direction = command.Direction,
            DefinitionJson = command.DefinitionJson,
            CreatedAt = command.CreatedAt,
            UpdatedAt = command.CreatedAt
        };
        db.Challenges.Add(entity);
        try
        {
            await db.SaveChangesAsync(ct);
            var result = await Project(db.Challenges.AsNoTracking()
                    .Where(challenge => challenge.Id == entity.Id))
                .SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return new(ChallengeTemplateWriteState.Succeeded, result);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            db.Entry(entity).State = EntityState.Detached;
            return new(ChallengeTemplateWriteState.ResourceIdConflict);
        }
    }

    public async Task<IReadOnlyList<ChallengeTemplateView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct) =>
        await Project(Authorized(
                    includeDeleted
                        ? db.Challenges.IgnoreQueryFilters().AsNoTracking()
                        : db.Challenges.AsNoTracking(),
                    actorId,
                    isAdministrator)
                .OrderByDescending(challenge => challenge.UpdatedAt)
                .ThenBy(challenge => challenge.Id))
            .ToListAsync(ct);

    public Task<ChallengeTemplateView?> FindAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = includeDeleted
            ? db.Challenges.IgnoreQueryFilters().AsNoTracking()
            : db.Challenges.AsNoTracking();
        return Project(Authorized(source, actorId, isAdministrator)
                .Where(challenge => challenge.Id == challengeId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ChallengeTemplateWriteResult> UpdateAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, command.ChallengeId, ct);
        var entity = await WriteAuthorized(db.Challenges, command.ActorId, command.IsAdministrator)
            .SingleOrDefaultAsync(challenge => challenge.Id == command.ChallengeId, ct);
        if (entity is null)
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        if (entity.Revision != command.ExpectedRevision)
            return new(ChallengeTemplateWriteState.RevisionConflict);
        if (entity.Mode != command.Mode
            && await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item =>
                    item.ChallengeId == command.ChallengeId
                    && item.DeletedAt == null,
                    ct))
        {
            return new(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
        }
        entity.Mode = command.Mode;
        entity.Visibility = command.Visibility;
        entity.Title = command.Title;
        entity.Description = command.Description;
        entity.Direction = command.Direction;
        entity.DefinitionJson = command.DefinitionJson;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = command.UpdatedAt;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    public async Task<ChallengeTemplateDeleteFailure?> SoftDeleteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        if (await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                item => item.ChallengeId == challengeId && item.DeletedAt == null,
                ct))
            return ChallengeTemplateDeleteFailure.InUse;
        if (!await SoftDeleteEntityAsync(
                challengeId,
                actorId,
                isAdministrator,
                now,
                ct))
            return ChallengeTemplateDeleteFailure.NotFound;
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<ChallengeTemplateWriteResult> RestoreAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        var entity = await WriteAuthorized(
                db.Challenges.IgnoreQueryFilters(),
                actorId,
                isAdministrator)
            .SingleOrDefaultAsync(challenge =>
                challenge.Id == challengeId && challenge.DeletedAt != null, ct);
        if (entity is null)
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            entity.ManagerIds.Append(entity.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }
        entity.DeletedAt = null;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    public async Task<ChallengeTemplateWriteResult> UpdatePermissionsAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid[] managerIds,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalized = managerIds.Distinct().Order().ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        var entity = await db.Challenges.SingleOrDefaultAsync(challenge =>
            challenge.Id == challengeId &&
            (isAdministrator || challenge.OwnerId == actorId), ct);
        if (entity is null)
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        if (entity.Revision != expectedRevision)
            return new(ChallengeTemplateWriteState.RevisionConflict);
        if (normalized.Contains(entity.OwnerId))
        {
            return new(
                ChallengeTemplateWriteState.OwnerIncludedInManagerSet,
                UserIds: [entity.OwnerId]);
        }
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            normalized.Append(entity.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }
        entity.ManagerIds = normalized;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    public async Task<ChallengeTemplateWriteResult> TransferOwnerAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        var entity = await db.Challenges.SingleOrDefaultAsync(challenge =>
            challenge.Id == challengeId &&
            (isAdministrator || challenge.OwnerId == actorId), ct);
        if (entity is null)
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        if (entity.Revision != expectedRevision)
            return new(ChallengeTemplateWriteState.RevisionConflict);
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
                ChallengeTemplateWriteState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                ChallengeTemplateWriteState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }
        entity.OwnerId = ownerId;
        entity.ManagerIds = managerIds;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    private async Task<bool> SoftDeleteEntityAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entity = await WriteAuthorized(db.Challenges, actorId, isAdministrator)
            .SingleOrDefaultAsync(challenge =>
                challenge.Id == challengeId && challenge.DeletedAt == null, ct);
        if (entity is null)
            return false;
        entity.DeletedAt = now;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static IQueryable<Challenge> Authorized(
        IQueryable<Challenge> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(challenge =>
                challenge.OwnerId == actorId ||
                challenge.ManagerIds.Contains(actorId) ||
                challenge.Visibility == ChallengeVisibility.Shared);

    private static IQueryable<Challenge> WriteAuthorized(
        IQueryable<Challenge> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(challenge =>
                challenge.OwnerId == actorId ||
                challenge.ManagerIds.Contains(actorId));

    private IQueryable<ChallengeTemplateView> Project(IQueryable<Challenge> source) =>
        source.Select(challenge => new ChallengeTemplateView(
            challenge.Id,
            challenge.OwnerId,
            challenge.ManagerIds,
            challenge.Mode,
            challenge.Visibility,
            challenge.Title,
            challenge.Description,
            challenge.Direction,
            challenge.DefinitionJson,
            challenge.Revision,
            challenge.DeletedAt,
            db.CompetitionChallenges.IgnoreQueryFilters().Count(item =>
                item.ChallengeId == challenge.Id && item.DeletedAt == null),
            challenge.CreatedAt,
            challenge.UpdatedAt));

}

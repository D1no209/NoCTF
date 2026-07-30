using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Challenges;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(NoCtfDbContext db) : IChallengeBankStore
{
    public async Task<ChallengeTemplateView?> CreateAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        var entity = new Challenge
        {
            Id = command.ChallengeId ?? Guid.CreateVersion7(command.CreatedAt),
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
            return await Project(db.Challenges.AsNoTracking()
                    .Where(challenge => challenge.Id == entity.Id))
                .SingleAsync(ct);
        }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;
            return null;
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
                isAdministrator))
            .OrderByDescending(challenge => challenge.UpdatedAt)
            .ThenBy(challenge => challenge.Id)
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

    public async Task<ChallengeTemplateView?> UpdateAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, command.ChallengeId, ct);
        var entity = await WriteAuthorized(db.Challenges, command.ActorId, command.IsAdministrator)
            .SingleOrDefaultAsync(challenge =>
                challenge.Id == command.ChallengeId &&
                challenge.Revision == command.ExpectedRevision, ct);
        if (entity is null)
            return null;
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
        return result;
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
        if (!await SetDeletedEntityAsync(
                challengeId,
                actorId,
                isAdministrator,
                now,
                restore: false,
                ct))
            return ChallengeTemplateDeleteFailure.NotFound;
        await transaction.CommitAsync(ct);
        return null;
    }

    public async Task<bool> RestoreAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ChallengeWriteLock.AcquireAsync(db, challengeId, ct);
        if (!await SetDeletedEntityAsync(
                challengeId,
                actorId,
                isAdministrator,
                now,
                restore: true,
                ct))
            return false;
        await transaction.CommitAsync(ct);
        return true;
    }

    public async Task<ChallengeTemplateView?> UpdatePermissionsAsync(
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
            challenge.Revision == expectedRevision &&
            (isAdministrator || challenge.OwnerId == actorId), ct);
        if (entity is null || normalized.Contains(entity.OwnerId))
            return null;
        entity.ManagerIds = normalized;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    public async Task<ChallengeTemplateView?> TransferOwnerAsync(
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
            challenge.Revision == expectedRevision &&
            (isAdministrator || challenge.OwnerId == actorId), ct);
        if (entity is null ||
            ownerId == Guid.Empty ||
            !await db.Users.AsNoTracking().AnyAsync(user => user.Id == ownerId, ct))
            return null;
        var previousOwnerId = entity.OwnerId;
        entity.OwnerId = ownerId;
        entity.ManagerIds = entity.ManagerIds
            .Append(previousOwnerId)
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return result;
    }

    private async Task<bool> SetDeletedEntityAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        var source = restore ? db.Challenges.IgnoreQueryFilters() : db.Challenges;
        var entity = await WriteAuthorized(source, actorId, isAdministrator)
            .SingleOrDefaultAsync(challenge =>
                challenge.Id == challengeId &&
                (restore ? challenge.DeletedAt != null : challenge.DeletedAt == null), ct);
        if (entity is null)
            return false;
        entity.DeletedAt = restore ? null : now;
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

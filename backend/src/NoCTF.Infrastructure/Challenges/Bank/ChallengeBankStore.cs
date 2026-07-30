using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(NoCtfDbContext db) : IChallengeBankStore
{
    public async Task<ChallengeTemplateView> CreateAsync(CreateChallengeTemplateCommand command, CancellationToken ct)
    {
        var entity = new Challenge
        {
            Id = command.Id ?? Guid.CreateVersion7(command.CreatedAt),
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
        await db.SaveChangesAsync(ct);
        return await MapAsync(entity, ct);
    }

    public async Task<IReadOnlyList<ChallengeTemplateView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct) =>
        await Authorized(db.Challenges.AsNoTracking(), actorId, isAdministrator)
            .OrderByDescending(challenge => challenge.UpdatedAt)
            .ThenBy(challenge => challenge.Id)
            .Select(challenge => new ChallengeTemplateView(
                challenge.Id, challenge.OwnerId, challenge.ManagerIds, challenge.Mode, challenge.Visibility,
                challenge.Title, challenge.Description, challenge.Direction, challenge.DefinitionJson, challenge.Revision,
                challenge.CreatedAt, challenge.UpdatedAt, challenge.DeletedAt,
                db.CompetitionChallenges.IgnoreQueryFilters().Count(instance =>
                    instance.ChallengeId == challenge.Id && instance.DeletedAt == null)))
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
        return Authorized(source, actorId, isAdministrator)
            .Where(challenge => challenge.Id == challengeId)
            .Select(challenge => new ChallengeTemplateView(
                challenge.Id, challenge.OwnerId, challenge.ManagerIds, challenge.Mode, challenge.Visibility,
                challenge.Title, challenge.Description, challenge.Direction, challenge.DefinitionJson, challenge.Revision,
                challenge.CreatedAt, challenge.UpdatedAt, challenge.DeletedAt,
                db.CompetitionChallenges.IgnoreQueryFilters().Count(instance =>
                    instance.ChallengeId == challenge.Id && instance.DeletedAt == null)))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ChallengeTemplateView?> UpdateAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct)
    {
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
        return await MapAsync(entity, ct);
    }

    public Task<bool> SoftDeleteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(challengeId, actorId, isAdministrator, now, restore: false, ct);

    public Task<bool> RestoreAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(challengeId, actorId, isAdministrator, now, restore: true, ct);

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
        return await MapAsync(entity, ct);
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
        var entity = await db.Challenges.SingleOrDefaultAsync(challenge =>
            challenge.Id == challengeId &&
            challenge.Revision == expectedRevision &&
            (isAdministrator || challenge.OwnerId == actorId), ct);
        if (entity is null || ownerId == Guid.Empty)
            return null;
        entity.OwnerId = ownerId;
        entity.ManagerIds = entity.ManagerIds.Where(id => id != ownerId).ToArray();
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await MapAsync(entity, ct);
    }

    private async Task<bool> SetDeletedAsync(
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

    private async Task<ChallengeTemplateView> MapAsync(
        Challenge challenge,
        CancellationToken ct)
    {
        var activeCompetitionReferenceCount = await db.CompetitionChallenges
            .IgnoreQueryFilters()
            .CountAsync(
                instance =>
                    instance.ChallengeId == challenge.Id
                    && instance.DeletedAt == null,
                ct);
        return new(
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
            challenge.CreatedAt,
            challenge.UpdatedAt,
            challenge.DeletedAt,
            activeCompetitionReferenceCount);
    }

}

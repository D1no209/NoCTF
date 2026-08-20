using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Bank;
using NoCTF.Domain.Challenges;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.GameModes.Registration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox? messageOutbox = null,
    ICompetitionEventRecorder? eventRecorder = null) : IChallengeBankStore
{
    private static readonly IChallengeRuntimeTemplateCatalog RuntimeTemplates =
        new ChallengeRuntimeTemplateCatalog();
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<ChallengeTemplateWriteResult> CreateAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        var challengeId = command.ChallengeId ?? Guid.CreateVersion7(command.CreatedAt);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            if (await db.Challenges.IgnoreQueryFilters().AsNoTracking()
                    .AnyAsync(challenge => challenge.Id == challengeId, ct))
                return new(ChallengeTemplateWriteState.ResourceIdConflict);
            throw;
        }
    }

    public async Task<IReadOnlyList<ChallengeTemplateView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = db.Challenges.IgnoreQueryFilters().AsNoTracking();
        if (!includeDeleted)
            source = source.Where(challenge => challenge.DeletedAt == null);

        return await Project(Authorized(source, actorId, isAdministrator)
                .OrderByDescending(challenge => challenge.UpdatedAt)
                .ThenBy(challenge => challenge.Id))
            .ToListAsync(ct);
    }

    public Task<ChallengeTemplateView?> FindAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = db.Challenges.IgnoreQueryFilters().AsNoTracking();
        if (!includeDeleted)
            source = source.Where(challenge => challenge.DeletedAt == null);

        return Project(Authorized(source, actorId, isAdministrator)
                .Where(challenge => challenge.Id == challengeId))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<ChallengeTemplateWriteResult> UpdateAsync(
        UpdateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var entity = await WriteAuthorized(db.Challenges, command.ActorId, command.IsAdministrator)
            .SingleOrDefaultAsync(challenge => challenge.Id == command.ChallengeId, ct);
        if (entity is null)
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        if (entity.Revision != command.ExpectedRevision)
            return new(ChallengeTemplateWriteState.RevisionConflict);
        if (HasSameEditableContent(entity, command))
        {
            var unchanged = await Project(db.Challenges.AsNoTracking()
                    .Where(challenge => challenge.Id == entity.Id))
                .SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return new(ChallengeTemplateWriteState.Succeeded, unchanged);
        }
        if (entity.Mode != command.Mode
            && await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item =>
                    item.ChallengeId == command.ChallengeId
                    && item.DeletedAt == null,
                    ct))
        {
            return new(ChallengeTemplateWriteState.ActiveCompetitionModeConflict);
        }
        var supportsRegularExpression = command.Mode == GameMode.Ctf
            && RuntimeTemplates.Get(command.Mode, command.DefinitionJson)?.FlagSource
                is null or RuntimeFlagSource.Static;
        if (!supportsRegularExpression)
        {
            var competitionChallengeIds = db.CompetitionChallenges.IgnoreQueryFilters()
                .Where(item => item.ChallengeId == command.ChallengeId)
                .Select(item => item.Id);
            var hasRegularExpressionFlags = await db.ChallengeFlags.AsNoTracking()
                .AnyAsync(flag =>
                    flag.DeletedAt == null
                    &&
                    flag.MatchKind == ChallengeFlagMatchKind.RegularExpression
                    && (flag.ChallengeId == command.ChallengeId
                        || flag.CompetitionChallengeId != null
                        && competitionChallengeIds.Contains(flag.CompetitionChallengeId.Value)),
                    ct);
            if (hasRegularExpressionFlags)
            {
                return new(
                    ChallengeTemplateWriteState.InvalidDefinition,
                    Detail: "Delete or convert regular-expression flags before enabling dynamic flags or changing the game mode.");
            }
        }
        var descriptionChanged = !string.Equals(
            entity.Description,
            command.Description,
            StringComparison.Ordinal);
        var catalogChanged = !string.Equals(entity.Title, command.Title, StringComparison.Ordinal)
            || !string.Equals(entity.Direction, command.Direction, StringComparison.Ordinal);
        var publishedReferences = descriptionChanged
            ? await db.CompetitionChallenges.AsNoTracking()
                .Where(item =>
                    item.ChallengeId == entity.Id
                    && item.IsPublished
                    && item.DeletedAt == null)
                .Select(item => new PublishedChallengeReference(item.CompetitionId, item.Id))
                .ToArrayAsync(ct)
            : Array.Empty<PublishedChallengeReference>();
        entity.Mode = command.Mode;
        entity.Visibility = command.Visibility;
        entity.Title = command.Title;
        entity.Description = command.Description;
        entity.Direction = command.Direction;
        entity.DefinitionJson = command.DefinitionJson;
        entity.Revision = checked(entity.Revision + 1);
        entity.UpdatedAt = command.UpdatedAt;
        foreach (var reference in publishedReferences)
        {
            await events.RecordAsync(new(
                reference.CompetitionId,
                CompetitionEventKind.ChallengeDescriptionUpdated,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Public,
                command.UpdatedAt,
                ActorUserId: command.ActorId,
                CompetitionChallengeId: reference.Id), ct);
        }
        try
        {
            await db.SaveChangesAsync(ct);
            if (catalogChanged)
            {
                _ = await db.Competitions
                    .Where(competition => db.CompetitionChallenges.IgnoreQueryFilters().Any(instance =>
                        instance.CompetitionId == competition.Id
                        && instance.ChallengeId == entity.Id
                        && instance.DeletedAt == null))
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(competition => competition.LeaderboardDirty, true), ct);
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ChallengeTemplateWriteState.RevisionConflict);
        }
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
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
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(ChallengeTemplateWriteState.RevisionConflict);
        }
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

    private static bool HasSameEditableContent(
        Challenge entity,
        UpdateChallengeTemplateCommand command) =>
        entity.Mode == command.Mode
        && entity.Visibility == command.Visibility
        && string.Equals(entity.Title, command.Title, StringComparison.Ordinal)
        && string.Equals(entity.Description, command.Description, StringComparison.Ordinal)
        && string.Equals(entity.Direction, command.Direction, StringComparison.Ordinal)
        && JsonEquals(entity.DefinitionJson, command.DefinitionJson);

    private static bool JsonEquals(string current, string updated)
    {
        if (string.Equals(current, updated, StringComparison.Ordinal))
            return true;
        try
        {
            return JsonNode.DeepEquals(JsonNode.Parse(current), JsonNode.Parse(updated));
        }
        catch (JsonException)
        {
            return false;
        }
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

    private sealed record PublishedChallengeReference(
        Guid CompetitionId,
        Guid Id);

}

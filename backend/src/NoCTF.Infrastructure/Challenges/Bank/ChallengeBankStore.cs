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
using NoCTF.GameModes.Ctf.Configuration;

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox? messageOutbox = null,
    ICompetitionEventRecorder? eventRecorder = null) : IChallengeBankStore
{
    private static readonly IChallengeRuntimeTemplateCatalog RuntimeTemplates =
        new ChallengeRuntimeTemplateCatalog();
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new NoOpTransactionalMessageOutbox();
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public async Task<ChallengeTemplateWriteResult> CreateAsync(
        CreateChallengeTemplateCommand command,
        CancellationToken ct)
    {
        var challengeId = command.ChallengeId ?? Guid.CreateVersion7(command.CreatedAt);
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
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

    public async Task<ChallengeTemplateListPage> ListPageAsync(
        ChallengeTemplateListQuery query,
        CancellationToken ct)
    {
        var source = db.Challenges.IgnoreQueryFilters().AsNoTracking();
        if (!query.IncludeDeleted)
            source = source.Where(challenge => challenge.DeletedAt == null);

        var authorized = Authorized(source, query.ActorId, query.IsAdministrator);
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        if (keyword is not null)
        {
            var pattern = $"%{keyword}%";
            authorized = authorized.Where(challenge =>
                EF.Functions.ILike(challenge.Title, pattern)
                || EF.Functions.ILike(challenge.Direction, pattern));
        }
        if (!string.IsNullOrWhiteSpace(query.Direction))
            authorized = authorized.Where(challenge => challenge.Direction == query.Direction);

        var total = await authorized.CountAsync(ct);
        var ordered = query.Desc
            ? authorized.OrderByDescending(challenge => challenge.UpdatedAt)
                .ThenByDescending(challenge => challenge.Id)
            : authorized.OrderBy(challenge => challenge.UpdatedAt)
                .ThenBy(challenge => challenge.Id);
        var items = await Project(ordered)
            .Skip(query.Offset)
            .Take(query.Limit)
            .ToListAsync(ct);

        var directions = await Authorized(source, query.ActorId, query.IsAdministrator)
            .Select(challenge => challenge.Direction)
            .Distinct()
            .OrderBy(direction => direction)
            .ToArrayAsync(ct);
        return new(items, total, directions);
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
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(
            db,
            command.ChallengeId,
            ct);
        if (entity is null
            || entity.DeletedAt is not null
            || !CanWrite(entity, command.ActorId, command.IsAdministrator))
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
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
        var currentInteraction = entity.Mode == GameMode.Ctf
            ? GetInteractionKind(entity.DefinitionJson)
            : CtfInteractionKind.FlagSubmission;
        var requestedInteraction = command.Mode == GameMode.Ctf
            ? GetInteractionKind(command.DefinitionJson)
            : CtfInteractionKind.FlagSubmission;
        if (requestedInteraction == CtfInteractionKind.PatchVerification
            && await db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(flag => flag.ChallengeId == command.ChallengeId, ct))
        {
            return new(
                ChallengeTemplateWriteState.InvalidDefinition,
                Detail: "PatchVerification challenges cannot contain static or generated flags.");
        }
        if (currentInteraction != requestedInteraction)
        {
            var referenced = await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.ChallengeId == command.ChallengeId, ct);
            var hasTemplateRuntime = await db.RuntimeInstances.AsNoTracking()
                .AnyAsync(runtime => runtime.ChallengeId == command.ChallengeId, ct);
            var hasGameplayFact = await db.GameplayFacts.AsNoTracking()
                .Join(
                    db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking(),
                    fact => fact.CompetitionChallengeId,
                    competitionChallenge => competitionChallenge.Id,
                    (fact, competitionChallenge) => competitionChallenge.ChallengeId)
                .AnyAsync(challengeId => challengeId == command.ChallengeId, ct);
            if (referenced || hasTemplateRuntime || hasGameplayFact)
            {
                return new(
                    ChallengeTemplateWriteState.InteractionKindConflict,
                    Detail: "The CTF interaction kind can change only before the template is referenced or used.");
            }
        }
        var definitionChanged = !JsonEquals(
            entity.DefinitionJson,
            command.DefinitionJson);
        if (definitionChanged
            && await db.RuntimeInstances.AsNoTracking().AnyAsync(runtime =>
                (runtime.State == NoCTF.Domain.Runtime.RuntimeState.Queued
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Provisioning
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running
                    || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Stopping)
                && (runtime.ChallengeId == command.ChallengeId
                    || runtime.CompetitionChallengeId != null
                    && db.CompetitionChallenges.IgnoreQueryFilters().Any(reference =>
                        reference.Id == runtime.CompetitionChallengeId
                        && reference.ChallengeId == command.ChallengeId)),
                ct))
        {
            return new(
                ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict,
                Detail:
                    "Stop every active Runtime created from this template before changing its technical definition.");
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
            || !string.Equals(entity.Direction, command.Direction, StringComparison.OrdinalIgnoreCase);
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
        await db.SaveChangesAsync(ct);
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
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is not null
            || !CanWrite(entity, actorId, isAdministrator))
            return ChallengeTemplateDeleteFailure.NotFound;
        if (await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().AnyAsync(
                item => item.ChallengeId == challengeId && item.DeletedAt == null,
                ct))
            return ChallengeTemplateDeleteFailure.InUse;
        if (await db.RuntimeInstances.AsNoTracking().AnyAsync(
                runtime => runtime.ChallengeId == challengeId
                    && runtime.Purpose == NoCTF.Domain.Runtime.RuntimePurpose.TemplateTest
                    && (runtime.State == NoCTF.Domain.Runtime.RuntimeState.Queued
                        || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Provisioning
                        || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Running
                        || runtime.State == NoCTF.Domain.Runtime.RuntimeState.Stopping),
                ct))
        {
            return ChallengeTemplateDeleteFailure.InUse;
        }
        entity.DeletedAt = now;
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
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
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is null
            || !CanWrite(entity, actorId, isAdministrator))
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
        DateTimeOffset now,
        CancellationToken ct)
    {
        var normalized = managerIds.Distinct().Order().ToArray();
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is not null
            || !(isAdministrator || entity.OwnerId == actorId))
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
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
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is not null
            || !(isAdministrator || entity.OwnerId == actorId))
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
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
        entity.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        var result = await Project(db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.Id))
            .SingleAsync(ct);
        await transaction.CommitAsync(ct);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    private static bool HasSameEditableContent(
        Challenge entity,
        UpdateChallengeTemplateCommand command) =>
        entity.Mode == command.Mode
        && entity.Visibility == command.Visibility
        && string.Equals(entity.Title, command.Title, StringComparison.Ordinal)
        && string.Equals(entity.Description, command.Description, StringComparison.Ordinal)
        && string.Equals(entity.Direction, command.Direction, StringComparison.OrdinalIgnoreCase)
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

    private static CtfInteractionKind GetInteractionKind(string definitionJson)
    {
        try
        {
            return CtfConfigurationParser.ParseDefinition(definitionJson).InteractionKind;
        }
        catch (GameModeConfigurationException)
        {
            return CtfInteractionKind.FlagSubmission;
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

    private static bool CanWrite(
        Challenge challenge,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
        || challenge.OwnerId == actorId
        || challenge.ManagerIds.Contains(actorId);

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
            challenge.DeletedAt,
            db.CompetitionChallenges.IgnoreQueryFilters().Count(item =>
                item.ChallengeId == challenge.Id && item.DeletedAt == null),
            challenge.CreatedAt,
            challenge.UpdatedAt));

    private sealed record PublishedChallengeReference(
        Guid CompetitionId,
        Guid Id);

}

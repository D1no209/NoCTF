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

namespace NoCTF.Infrastructure.Challenges.Bank;

public sealed class ChallengeBankStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher? messageOutbox = null,
    ICompetitionEventRecorder? eventRecorder = null,
    NoCTF.Application.Challenges.Configuration.IChallengeMaterialMutationGate? material = null) : IChallengeBankStore
{
    private static readonly IChallengeRuntimeTemplateCatalog RuntimeTemplates =
        new ChallengeRuntimeTemplateCatalog();
    private readonly IPostCommitMessagePublisher outbox =
        messageOutbox ?? new NoOpPostCommitMessagePublisher();
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

        if (command.Definition.Questionnaire is { } questionnaire)
        {
            questionnaire.CurrentRevision.Id = Guid.CreateVersion7(command.CreatedAt);
            questionnaire.CurrentRevisionId = questionnaire.CurrentRevision.Id;
            questionnaire.CurrentRevision.Number = 1;
            questionnaire.CurrentRevision.CreatedAt = command.CreatedAt;
        }
        ChallengeDefinitionGraph.AssignChallengeId(command.Definition, challengeId);
        await EnsureQuestionnaireSourcesAsync(command.Definition, ct);
        var entity = new Challenge();
        entity.Id = challengeId;
        entity.OwnerId = command.OwnerId;
        entity.Visibility = command.Visibility;
        entity.Title = command.Title;
        entity.Description = command.Description;
        entity.Direction = command.Direction;
        entity.Definition = command.Definition;
        entity.CreatedAt = command.CreatedAt;
        entity.UpdatedAt = command.CreatedAt;
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

    public async Task<ChallengeTemplateListPage> ListPageAsync(
        ChallengeTemplateListQuery query,
        CancellationToken ct)
    {
        var source = db.Challenges.IgnoreQueryFilters().AsNoTracking().IgnoreAutoIncludes().Where(x => x.CatalogScope == ChallengeCatalogScope.QuestionBank);
        if (!query.IncludeDeleted)
            source = source.Where(challenge => challenge.DeletedAt == null);
        foreach (var required in query.RequiredCapabilities ?? [])
            source = source.Where(challenge => challenge.Definition!.Capabilities.Any(capability => capability.Kind == required));
        if (query.OnlyMine)
            source = source.Where(challenge => challenge.OwnerId == query.ActorId);

        var authorized = Authorized(source, query.ActorId, query.IsAdministrator);
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        if (keyword is not null)
        {
            var normalized = keyword.ToUpperInvariant();
            authorized = authorized.Where(challenge =>
                challenge.NormalizedTitle.Contains(normalized)
                || challenge.NormalizedDirection.Contains(normalized));
        }
        if (!string.IsNullOrWhiteSpace(query.Direction))
            authorized = authorized.Where(challenge => challenge.Direction == query.Direction);

        var total = await authorized.CountAsync(ct);
        var ordered = query.Desc
            ? authorized.OrderByDescending(challenge => challenge.UpdatedAt)
                .ThenByDescending(challenge => challenge.Id)
            : authorized.OrderBy(challenge => challenge.UpdatedAt)
                .ThenBy(challenge => challenge.Id);
        var items = await ProjectSummary(ordered)
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
        var source = db.Challenges.IgnoreQueryFilters().AsNoTracking().Where(x => x.CatalogScope == ChallengeCatalogScope.QuestionBank);
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
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, System.Data.IsolationLevel.Serializable, ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(
            db,
            command.ChallengeId,
            ct);
        if (entity is null
            || entity.DeletedAt is not null
            || entity.CatalogScope != ChallengeCatalogScope.QuestionBank
            || !CanWrite(entity, command.ActorId, command.IsAdministrator))
            return new(ChallengeTemplateWriteState.NotFoundOrForbidden);
        if (command.Definition.Questionnaire is { } requestedQuestionnaire)
        {
            var previous = entity.Definition?.Questionnaire;
            if (previous is not null && ChallengeDefinitionStructuralComparer.QuestionnaireContentEquals(previous.CurrentRevision, requestedQuestionnaire.CurrentRevision))
            {
                requestedQuestionnaire.CurrentRevision = previous.CurrentRevision;
                requestedQuestionnaire.CurrentRevisionId = previous.CurrentRevisionId;
            }
            else
            {
                requestedQuestionnaire.CurrentRevision.Id = Guid.CreateVersion7(command.UpdatedAt);
                requestedQuestionnaire.CurrentRevisionId = requestedQuestionnaire.CurrentRevision.Id;
                requestedQuestionnaire.CurrentRevision.Number = checked((previous?.CurrentRevision.Number ?? 0) + 1);
                requestedQuestionnaire.CurrentRevision.CreatedAt = command.UpdatedAt;
            }
        }
        if (HasSameEditableContent(entity, command))
        {
            var unchanged = await Project(db.Challenges.AsNoTracking()
                    .Where(challenge => challenge.Id == entity.Id))
                .SingleAsync(ct);
            await transaction.CommitAsync(ct);
            return new(ChallengeTemplateWriteState.Succeeded, unchanged);
        }
        var previousDefinition = entity.Definition!;
        var runtimeChanged = ChallengeDefinitionStructuralComparer.HasRuntimeChanges(previousDefinition, command.Definition);
        var changedCapabilities = ChallengeDefinitionStructuralComparer.ChangedExecutionCapabilities(previousDefinition, command.Definition);
        var displayChanged = entity.Title != command.Title || entity.Description != command.Description
            || !string.Equals(entity.Direction, command.Direction, StringComparison.OrdinalIgnoreCase);
        if (material is not null) await material.RequireMutableAsync(new(command.ChallengeId, null,
            displayChanged ? null : changedCapabilities, runtimeChanged), ct);
        if (command.Definition.Break is null
            && await db.ChallengeFlags.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(flag => flag.ChallengeId == command.ChallengeId, ct))
        {
            return new(
                ChallengeTemplateWriteState.InvalidDefinition,
                Detail: "Remove the template flags before removing its Break capability.");
        }
        if (await new ChallengeCapabilityUsageGuard(db).CheckAsync(command.ChallengeId, previousDefinition, command.Definition, ct) is { } conflict)
            return new(conflict, Detail: conflict switch
            {
                ChallengeTemplateWriteState.ReferencedCapabilityConflict => "Referenced capabilities cannot be removed.",
                ChallengeTemplateWriteState.PendingExecutionConflict => "Wait for the accepted capability executions to finish before changing their definitions.",
                ChallengeTemplateWriteState.ActiveRuntimeDefinitionConflict => "Stop the Runtime using the changed technical configuration before updating it.",
                _ => throw new ArgumentOutOfRangeException(nameof(conflict), conflict, null)
            });
        var definitionChanged = !ChallengeDefinitionStructuralComparer.Equals(
            entity.Definition,
            command.Definition);
        var supportsRegularExpression = StaticFlagConfiguration.SupportsRegularExpression(command.Definition, RuntimeTemplates);
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
                    Detail: "Delete or convert regular-expression flags before enabling dynamic flags.");
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
        entity.Visibility = command.Visibility;
        entity.Title = command.Title;
        entity.Description = command.Description;
        entity.Direction = command.Direction;
        if (definitionChanged)
        {
            if (entity.Definition is not null)
            {
                db.Remove(entity.Definition);
                await db.SaveChangesAsync(ct);
            }
            ChallengeDefinitionGraph.AssignChallengeId(command.Definition, entity.Id);
            await EnsureQuestionnaireSourcesAsync(command.Definition, ct);
            entity.Definition = command.Definition;
            db.Add(command.Definition);
        }
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
        await transaction.FlushMessagesAsync(outbox);
        return new(ChallengeTemplateWriteState.Succeeded, result);
    }

    public async Task<ChallengeTemplateDeleteFailure?> SoftDeleteAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await SoftDeleteOnceAsync(
                    challengeId, actorId, isAdministrator, now, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<ChallengeTemplateDeleteFailure?> SoftDeleteOnceAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.Serializable,
            ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is not null
            || entity.CatalogScope != ChallengeCatalogScope.QuestionBank
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
            || entity.CatalogScope != ChallengeCatalogScope.QuestionBank
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
            || entity.CatalogScope != ChallengeCatalogScope.QuestionBank
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
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await TransferOwnerOnceAsync(
                    challengeId, actorId, isAdministrator, ownerId, now, ct);
            }
            catch (Exception exception) when (attempt < 2
                && (exception is DbUpdateException
                    || RelationalRetry.IsTransientConcurrency(exception)))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<ChallengeTemplateWriteResult> TransferOwnerOnceAsync(
        Guid challengeId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.Serializable,
            ct);
        var entity = await ChallengeTemplateCriticalSection.AcquireAsync(db, challengeId, ct);
        if (entity is null
            || entity.DeletedAt is not null
            || entity.CatalogScope != ChallengeCatalogScope.QuestionBank
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
        entity.Visibility == command.Visibility
        && string.Equals(entity.Title, command.Title, StringComparison.Ordinal)
        && string.Equals(entity.Description, command.Description, StringComparison.Ordinal)
        && string.Equals(entity.Direction, command.Direction, StringComparison.OrdinalIgnoreCase)
        && ChallengeDefinitionStructuralComparer.Equals(entity.Definition, command.Definition);

    private static IQueryable<Challenge> Authorized(
        IQueryable<Challenge> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(challenge =>
                challenge.OwnerId == actorId ||
                challenge.Managers.Any(manager => manager.UserId == actorId) ||
                challenge.Visibility == ChallengeVisibility.Shared);

    private static IQueryable<Challenge> WriteAuthorized(
        IQueryable<Challenge> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(challenge =>
                challenge.OwnerId == actorId ||
                challenge.Managers.Any(manager => manager.UserId == actorId));

    private static bool CanWrite(
        Challenge challenge,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
        || challenge.OwnerId == actorId
        || challenge.Managers.Any(manager => manager.UserId == actorId);

    private IQueryable<ChallengeTemplateView> Project(IQueryable<Challenge> source) =>
        source.Select(challenge => new ChallengeTemplateView(
            challenge.Id,
            challenge.OwnerId,
            challenge.Managers.Select(manager => manager.UserId).ToArray(),
            challenge.Definition!.Capabilities.Select(item => item.Kind).ToArray(),
            challenge.Visibility,
            challenge.Title,
            challenge.Description,
            challenge.Direction,
            challenge.Definition!,
            challenge.DeletedAt,
            db.CompetitionChallenges.IgnoreQueryFilters().Count(item =>
                item.ChallengeId == challenge.Id && item.DeletedAt == null),
            challenge.CreatedAt,
            challenge.UpdatedAt) { ConcurrencyStamp = challenge.ConcurrencyStamp }).AsSplitQuery();

    private IQueryable<ChallengeTemplateSummaryView> ProjectSummary(IQueryable<Challenge> source) =>
        source.Select(challenge => new ChallengeTemplateSummaryView(
            challenge.Id,
            challenge.Definition!.Capabilities.Select(item => item.Kind).ToArray(),
            challenge.Visibility,
            challenge.Title,
            challenge.Direction,
            challenge.DeletedAt,
            db.CompetitionChallenges.IgnoreQueryFilters().Count(item =>
                item.ChallengeId == challenge.Id && item.DeletedAt == null),
            challenge.UpdatedAt) { ConcurrencyStamp = challenge.ConcurrencyStamp });

    private sealed record PublishedChallengeReference(
        Guid CompetitionId,
        Guid Id);

    private async Task EnsureQuestionnaireSourcesAsync(ChallengeDefinition definition, CancellationToken ct)
    {
        if (definition.Questionnaire is not { } questionnaire) return;
        var sources = questionnaire.CurrentRevision.Questions.Select(question => question.SourceId).Distinct().ToArray();
        var existing = await db.Set<NoCTF.Domain.Challenges.Capabilities.QuestionnaireQuestionSource>().Where(source => sources.Contains(source.Id))
            .Select(source => source.Id).ToArrayAsync(ct);
        foreach (var source in sources.Except(existing))
            db.Add(new NoCTF.Domain.Challenges.Capabilities.QuestionnaireQuestionSource { Id = source });
    }

}

using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Runtime.Provisioning;
using NoCTF.Domain.Competitions.Events;
using NoCTF.GameModes.Ctf.Configuration;
using NoCTF.GameModes.Registration;
using NoCTF.Application.Challenges.Bank;

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class ChallengeManagementStore(
    NoCtfDbContext db,
    IPostCommitMessagePublisher outbox,
    IChallengeRuntimeTemplateCatalog runtimeTemplates,
    ICompetitionEventRecorder? eventRecorder = null,
    IExperimentalFeatureReader? experimentalFeatures = null) : IChallengeManagementStore
{
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;

    public Task<ChallengeCompetitionContext?> GetCompetitionAsync(Guid competitionId, CancellationToken ct) =>
        db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new ChallengeCompetitionContext(competition.Mode, competition.Status))
            .SingleOrDefaultAsync(ct);

    public async Task<ChallengeMutationResult> CreateAsync(
        CreateCompetitionChallengeCommand command,
        CompetitionChallengeRules rules,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await CreateOnceAsync(command, rules, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<ChallengeMutationResult> CreateOnceAsync(
        CreateCompetitionChallengeCommand command,
        CompetitionChallengeRules rules,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.Serializable,
            ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);
        var competitionMode = await db.Competitions.AsNoTracking()
            .Where(competition => competition.Id == command.CompetitionId)
            .Select(competition => competition.Mode)
            .SingleAsync(ct);
        if (command.CompetitionChallengeId is Guid requestedId
            && await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == requestedId, ct))
            return new(null, ChallengeMutationFailure.ResourceIdConflict);

        var template = await ChallengeTemplateCriticalSection.AcquireAsync(
            db,
            command.ChallengeId,
            ct);
        if (template is null || template.DeletedAt is not null)
            return new(null, ChallengeMutationFailure.TemplateNotFound);
        if (template.Mode != competitionMode)
            return new(null, ChallengeMutationFailure.TemplateModeMismatch);
        if (GetInteractionKind(template.Definition)
                == CtfInteractionKind.PatchVerification
            && !(await IsPatchVerificationEnabledAsync(ct)))
        {
            return new(null, ChallengeMutationFailure.ExperimentalFeatureDisabled);
        }
        var entityId = command.CompetitionChallengeId ?? Guid.CreateVersion7(command.CreatedAt);
        if (rules.Mode != template.Mode)
            return new(null, ChallengeMutationFailure.TemplateModeMismatch);
        rules.CompetitionChallengeId = entityId;
        var entity = CompetitionChallengeGeneratedCatalog.Create(template.Mode);
        entity.Id = entityId;
        entity.CompetitionId = command.CompetitionId;
        entity.ChallengeId = command.ChallengeId;
        entity.Direction = await ResolveDirectionAsync(command.CompetitionId, template.Direction, ct);
        entity.DirectionId = entity.Direction.Id;
        entity.CustomTitle = command.CustomTitle;
        entity.Order = command.Order;
        entity.Rules = rules;
        entity.UpdatedAt = command.CreatedAt;
        db.CompetitionChallenges.Add(entity);
        await events.RecordAsync(new(
            entity.CompetitionId,
            CompetitionEventKind.ChallengeCreated,
            CompetitionEventLevel.Information,
            CompetitionEventVisibility.Staff,
            command.CreatedAt,
            CompetitionChallengeId: entity.Id), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(outbox);
            return new(Map(entity, template));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var failure = await FindCompetitionChallengeConflictAsync(
                entity.Id,
                entity.CompetitionId,
                entity.ChallengeId,
                entity.Order,
                includeIdConflict: true,
                ct);
            if (failure is null)
                throw;
            return new(null, failure.Value);
        }
    }

    public async Task<ChallengeView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct)
    {
        var projection = await Query(
                includeUnpublished,
                includeDeleted,
                competitionId,
                competitionChallengeId)
            .SingleOrDefaultAsync(ct);
        return projection is null ? null : Map(projection);
    }

    public async Task<IReadOnlyList<CompetitionChallengeSummaryView>> ListAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct)
    {
        var instances = includeDeleted
            ? db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking().IgnoreAutoIncludes()
            : db.CompetitionChallenges.AsNoTracking().IgnoreAutoIncludes();
        var templates = includeDeleted
            ? db.Challenges.IgnoreQueryFilters().AsNoTracking().IgnoreAutoIncludes()
            : db.Challenges.AsNoTracking().IgnoreAutoIncludes();
        return await instances.Where(instance => instance.CompetitionId == competitionId)
            .Join(templates,
                instance => instance.ChallengeId,
                template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .Where(item => includeUnpublished || item.Instance.IsPublished)
            .OrderBy(item => item.Instance.Order)
            .ThenBy(item => item.Instance.Id)
            .Select(item => new CompetitionChallengeSummaryView(
                item.Instance.Id,
                item.Instance.CompetitionId,
                item.Instance.ChallengeId,
                item.Instance.CustomTitle ?? item.Template.Title,
                item.Instance.CustomTitle,
                item.Instance.Direction != null ? item.Instance.Direction.Name : item.Template.Direction,
                item.Instance.Order,
                item.Instance.IsPublished,
                item.Instance.DeletedAt,
                db.Set<CtfChallengeDefinition>().AsNoTracking()
                    .Where(definition => definition.ChallengeId == item.Template.Id)
                    .Select(definition => definition.InteractionKind)
                    .FirstOrDefault(),
                item.Instance.DirectionId,
                item.Instance.Direction != null ? item.Instance.Direction.Icon : null))
            .ToArrayAsync(ct);
    }

    public async Task<ChallengeMutationResult> UpdateAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);

        var entity = await db.CompetitionChallenges
            .AsSplitQuery()
            .SingleOrDefaultAsync(item =>
                item.Id == command.CompetitionChallengeId &&
                item.CompetitionId == command.CompetitionId, ct);
        if (entity is null)
            return new(null, ChallengeMutationFailure.ChallengeNotFound);
        if (command.DirectionId is Guid directionId)
        {
            var direction = await db.Set<NoCTF.Domain.Competitions.Directions.CompetitionDirection>()
                .SingleOrDefaultAsync(item => item.CompetitionId == command.CompetitionId && item.Id == directionId, ct);
            if (direction is null) return new(null, ChallengeMutationFailure.InvalidDirection);
            entity.Direction = direction;
            entity.DirectionId = direction.Id;
        }
        var wasPublished = entity.IsPublished;
        var becamePublished = !wasPublished && command.IsPublished;
        if (becamePublished)
        {
            var definition = await db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.ChallengeId)
                .Select(challenge => new { challenge.Mode, challenge.Definition })
                .AsSplitQuery()
                .SingleAsync(ct);
            if (GetInteractionKind(definition.Definition)
                    == CtfInteractionKind.PatchVerification
                && !(await IsPatchVerificationEnabledAsync(ct)))
            {
                return new(null, ChallengeMutationFailure.ExperimentalFeatureDisabled);
            }
        }
        entity.CustomTitle = command.CustomTitle;
        entity.Order = command.Order;
        entity.IsPublished = command.IsPublished;
        entity.UpdatedAt = command.UpdatedAt;
        var eventKind = (wasPublished, command.IsPublished) switch
        {
            (false, true) => CompetitionEventKind.ChallengePublished,
            (true, false) => CompetitionEventKind.ChallengeUnpublished,
            _ => CompetitionEventKind.ChallengeUpdated
        };
        await events.RecordAsync(new(
            entity.CompetitionId,
            eventKind,
            CompetitionEventLevel.Information,
            command.IsPublished
                ? CompetitionEventVisibility.Public
                : CompetitionEventVisibility.Staff,
            command.UpdatedAt,
            CompetitionChallengeId: entity.Id), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            if (becamePublished)
            {
                var publishedTemplate = await db.Challenges.AsNoTracking()
                    .AsSplitQuery()
                    .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
                await outbox.PublishAsync(new ChallengePublished(
                    command.CompetitionId,
                    entity.Id,
                    entity.CustomTitle ?? publishedTemplate.Title,
                    entity.Direction?.Name ?? publishedTemplate.Direction,
                    command.UpdatedAt));
            }
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(outbox);
            var template = await db.Challenges.AsNoTracking()
                .AsSplitQuery()
                .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
            return new(Map(entity, template));
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var failure = await FindCompetitionChallengeConflictAsync(
                entity.Id,
                entity.CompetitionId,
                entity.ChallengeId,
                entity.Order,
                includeIdConflict: false,
                ct);
            if (failure is null)
                throw;
            return new(null, failure.Value);
        }
    }

    public Task<ChallengeMutationFailure?> SoftDeleteAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(
            competitionId,
            competitionChallengeId,
            now,
            restore: false,
            ct);

    public Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(
            competitionId,
            competitionChallengeId,
            now,
            restore: true,
            ct);

    private async Task<ChallengeMutationFailure?> SetDeletedAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        DateTimeOffset now,
        bool restore,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return ChallengeMutationFailure.CompetitionNotFound;

        var entity = await db.CompetitionChallenges.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item =>
            item.Id == competitionChallengeId &&
            item.CompetitionId == competitionId, ct);
        if (entity is null)
            return ChallengeMutationFailure.ChallengeNotFound;
        if ((entity.DeletedAt is null) == restore)
            return ChallengeMutationFailure.LifecycleStateConflict;
        if (restore)
        {
            var templateMode = await db.Challenges.AsNoTracking()
                .Where(challenge => challenge.Id == entity.ChallengeId)
                .Select(challenge => (GameMode?)challenge.Mode)
                .SingleOrDefaultAsync(ct);
            if (templateMode is null)
                return ChallengeMutationFailure.TemplateNotFound;
            var competitionMode = await db.Competitions.AsNoTracking()
                .Where(competition => competition.Id == competitionId)
                .Select(competition => competition.Mode)
                .SingleAsync(ct);
            if (templateMode != competitionMode)
                return ChallengeMutationFailure.TemplateModeMismatch;
        }

        entity.DeletedAt = restore ? null : now;
        entity.UpdatedAt = now;
        await events.RecordAsync(new(
            entity.CompetitionId,
            restore
                ? CompetitionEventKind.ChallengeUpdated
                : CompetitionEventKind.ChallengeDeleted,
            restore
                ? CompetitionEventLevel.Information
                : CompetitionEventLevel.Warning,
            CompetitionEventVisibility.Staff,
            now,
            CompetitionChallengeId: entity.Id), ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await transaction.FlushMessagesAsync(outbox);
            return null;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var failure = await FindCompetitionChallengeConflictAsync(
                entity.Id,
                entity.CompetitionId,
                entity.ChallengeId,
                entity.Order,
                includeIdConflict: false,
                ct);
            if (failure is null)
                throw;
            return failure.Value;
        }
    }

    private IQueryable<ChallengeProjection> Query(
        bool includeUnpublished,
        bool includeDeleted,
        Guid? competitionId = null,
        Guid? competitionChallengeId = null)
    {
        var instances = includeDeleted
            ? db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
            : db.CompetitionChallenges.AsNoTracking();
        var templates = includeDeleted
            ? db.Challenges.IgnoreQueryFilters().AsNoTracking()
            : db.Challenges.AsNoTracking();
        if (competitionId is Guid actualCompetitionId)
            instances = instances.Where(item => item.CompetitionId == actualCompetitionId);
        if (competitionChallengeId is Guid actualCompetitionChallengeId)
            instances = instances.Where(item => item.Id == actualCompetitionChallengeId);
        return instances
            .Join(
                templates,
                instance => instance.ChallengeId,
                template => template.Id,
                (instance, template) => new { Instance = instance, Template = template })
            .Where(item => includeUnpublished || item.Instance.IsPublished)
            .OrderBy(item => item.Instance.Order)
            .ThenBy(item => item.Instance.Id)
            .Select(item => new ChallengeProjection(
                item.Instance.Id,
                item.Instance.CompetitionId,
                item.Instance.ChallengeId,
                item.Instance.CustomTitle ?? item.Template.Title,
                item.Instance.CustomTitle,
                item.Template.Description,
                item.Instance.Direction != null ? item.Instance.Direction.Name : item.Template.Direction,
                item.Instance.Order,
                item.Instance.IsPublished,
                item.Instance.DeletedAt,
                item.Template.Mode,
                item.Template.Definition,
                item.Template.CreatedAt,
                item.Instance.UpdatedAt,
                item.Instance.DirectionId,
                item.Instance.Direction != null ? item.Instance.Direction.Icon : null))
            .AsSplitQuery();
    }

    private ChallengeView Map(CompetitionChallenge instance, Challenge template)
    {
        var runtime = runtimeTemplates.Get(template.Definition);
        return new(
            instance.Id,
            instance.CompetitionId,
            instance.ChallengeId,
            instance.CustomTitle ?? template.Title,
            instance.CustomTitle,
            template.Description,
            instance.Direction?.Name ?? template.Direction,
            instance.Order,
            instance.IsPublished,
            instance.DeletedAt,
            runtime is not null,
            template.CreatedAt,
            instance.UpdatedAt)
        {
            DirectionId = instance.DirectionId,
            DirectionIcon = instance.Direction?.Icon,
            UsesDynamicFlag = runtime is { FlagSource: not RuntimeFlagSource.Static },
            InteractionKind = GetInteractionKind(template.Definition)
        };
    }

    private ChallengeView Map(ChallengeProjection projection)
    {
        var runtime = runtimeTemplates.Get(projection.Definition);
        return new(
            projection.Id,
            projection.CompetitionId,
            projection.ChallengeId,
            projection.Title,
            projection.CustomTitle,
            projection.Description,
            projection.Direction,
            projection.Order,
            projection.IsPublished,
            projection.DeletedAt,
            runtime is not null,
            projection.CreatedAt,
            projection.UpdatedAt)
        {
            DirectionId = projection.DirectionId,
            DirectionIcon = projection.DirectionIcon,
            UsesDynamicFlag = runtime is { FlagSource: not RuntimeFlagSource.Static },
            InteractionKind = GetInteractionKind(projection.Definition)
        };
    }

    private async Task<NoCTF.Domain.Competitions.Directions.CompetitionDirection> ResolveDirectionAsync(Guid competitionId, string name, CancellationToken ct)
    {
        var canonical = NoCTF.Domain.Competitions.Directions.CompetitionDirectionDefaults.CanonicalName(name);
        var direction = await db.Set<NoCTF.Domain.Competitions.Directions.CompetitionDirection>()
            .Where(item => item.CompetitionId == competitionId && (item.TemplateDirection ?? item.NormalizedName) == canonical)
            .OrderByDescending(item => item.TemplateDirection == canonical).ThenBy(item => item.Position).FirstOrDefaultAsync(ct);
        if (direction is not null) return direction;
        // Importing a template must not recreate directions the competition removed.
        return await db.Set<NoCTF.Domain.Competitions.Directions.CompetitionDirection>()
            .Where(item => item.CompetitionId == competitionId)
            .OrderByDescending(item => item.NormalizedName == "MISC").ThenBy(item => item.Position).FirstAsync(ct);
    }

    private static CtfInteractionKind GetInteractionKind(ChallengeDefinition? definition) =>
        definition is CtfChallengeDefinition ctf
            ? ctf.InteractionKind
            : CtfInteractionKind.FlagSubmission;

    private Task<bool> IsPatchVerificationEnabledAsync(CancellationToken ct) =>
        experimentalFeatures?.IsCtfPatchVerificationEnabledAsync(ct)
        ?? Task.FromResult(false);

    private sealed record ChallengeProjection(
        Guid Id,
        Guid CompetitionId,
        Guid ChallengeId,
        string Title,
        string? CustomTitle,
        string? Description,
        string Direction,
        int Order,
        bool IsPublished,
        DateTimeOffset? DeletedAt,
        GameMode Mode,
        ChallengeDefinition? Definition,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        Guid? DirectionId,
        string? DirectionIcon);

    private async Task<ChallengeMutationFailure?> FindCompetitionChallengeConflictAsync(
        Guid id,
        Guid competitionId,
        Guid challengeId,
        int order,
        bool includeIdConflict,
        CancellationToken ct)
    {
        if (includeIdConflict && await db.CompetitionChallenges.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(item => item.Id == id, ct))
            return ChallengeMutationFailure.ResourceIdConflict;
        if (await db.CompetitionChallenges.AsNoTracking().AnyAsync(item =>
                item.Id != id
                && item.CompetitionId == competitionId
                && item.ChallengeId == challengeId, ct))
            return ChallengeMutationFailure.ChallengeTemplateConflict;
        if (await db.CompetitionChallenges.AsNoTracking().AnyAsync(item =>
                item.Id != id
                && item.CompetitionId == competitionId
                && item.Order == order, ct))
            return ChallengeMutationFailure.ChallengeOrderConflict;
        return null;
    }
}

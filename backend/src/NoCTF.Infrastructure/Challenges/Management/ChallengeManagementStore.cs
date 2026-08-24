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

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class ChallengeManagementStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
    IChallengeRuntimeTemplateCatalog runtimeTemplates,
    ICompetitionEventRecorder? eventRecorder = null) : IChallengeManagementStore
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
        string configurationJson,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
        var entity = new CompetitionChallenge
        {
            Id = command.CompetitionChallengeId ?? Guid.CreateVersion7(command.CreatedAt),
            CompetitionId = command.CompetitionId,
            ChallengeId = command.ChallengeId,
            CustomTitle = command.CustomTitle,
            BaseScore = command.BaseScore,
            Order = command.Order,
            RulesJson = configurationJson,
            UpdatedAt = command.CreatedAt
        };
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
            await outbox.FlushOutgoingMessagesAsync();
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

    public async Task<IReadOnlyList<ChallengeView>> ListAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct) =>
        (await Query(includeUnpublished, includeDeleted, competitionId)
            .ToListAsync(ct))
            .Select(Map)
            .ToArray();

    public async Task<ChallengeMutationResult> UpdateAsync(
        UpdateCompetitionChallengeCommand command,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct) is null)
            return new(null, ChallengeMutationFailure.CompetitionNotFound);

        var entity = await db.CompetitionChallenges
            .SingleOrDefaultAsync(item =>
                item.Id == command.CompetitionChallengeId &&
                item.CompetitionId == command.CompetitionId, ct);
        if (entity is null)
            return new(null, ChallengeMutationFailure.ChallengeNotFound);
        var wasPublished = entity.IsPublished;
        var becamePublished = !wasPublished && command.IsPublished;
        entity.BaseScore = command.BaseScore;
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
                    .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
                await outbox.PublishAsync(new ChallengePublished(
                    command.CompetitionId,
                    entity.Id,
                    entity.CustomTitle ?? publishedTemplate.Title,
                    publishedTemplate.Direction,
                    command.UpdatedAt));
            }
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            var template = await db.Challenges.AsNoTracking()
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
            await outbox.FlushOutgoingMessagesAsync();
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
                item.Template.Direction,
                item.Instance.BaseScore,
                item.Instance.Order,
                item.Instance.IsPublished,
                item.Instance.DeletedAt,
                item.Template.Mode,
                item.Template.DefinitionJson,
                item.Template.CreatedAt,
                item.Instance.UpdatedAt));
    }

    private ChallengeView Map(CompetitionChallenge instance, Challenge template) =>
        new(
            instance.Id,
            instance.CompetitionId,
            instance.ChallengeId,
            instance.CustomTitle ?? template.Title,
            instance.CustomTitle,
            template.Description,
            template.Direction,
            instance.BaseScore,
            instance.Order,
            instance.IsPublished,
            instance.DeletedAt,
            runtimeTemplates.Get(template.Mode, template.DefinitionJson) is not null,
            template.CreatedAt,
            instance.UpdatedAt);

    private ChallengeView Map(ChallengeProjection projection) =>
        new(
            projection.Id,
            projection.CompetitionId,
            projection.ChallengeId,
            projection.Title,
            projection.CustomTitle,
            projection.Description,
            projection.Direction,
            projection.BaseScore,
            projection.Order,
            projection.IsPublished,
            projection.DeletedAt,
            runtimeTemplates.Get(projection.Mode, projection.DefinitionJson) is not null,
            projection.CreatedAt,
            projection.UpdatedAt);

    private sealed record ChallengeProjection(
        Guid Id,
        Guid CompetitionId,
        Guid ChallengeId,
        string Title,
        string? CustomTitle,
        string? Description,
        string Direction,
        long BaseScore,
        int Order,
        bool IsPublished,
        DateTimeOffset? DeletedAt,
        GameMode Mode,
        string DefinitionJson,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

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

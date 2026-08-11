using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Challenges.Management;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Competitions;
using NoCTF.Infrastructure.Challenges;
using NoCTF.Application.Messaging;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Challenges.Management;

public sealed class ChallengeManagementStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox outbox,
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

        var template = await db.Challenges.AsNoTracking()
            .SingleOrDefaultAsync(challenge => challenge.Id == command.ChallengeId, ct);
        if (template is null)
            return new(null, ChallengeMutationFailure.TemplateNotFound);
        if (template.Mode != competitionMode)
            return new(null, ChallengeMutationFailure.TemplateModeMismatch);
        var templateFence = await db.Challenges
            .Where(challenge =>
                challenge.Id == command.ChallengeId
                && challenge.Mode == competitionMode
                && challenge.Revision == template.Revision)
            .ExecuteUpdateAsync(setters => setters.SetProperty(
                challenge => challenge.Revision,
                challenge => challenge.Revision + 1), ct);
        if (templateFence == 0)
            return new(null, ChallengeMutationFailure.RevisionConflict);
        template.Revision = checked(template.Revision + 1);
        var trackedTemplate = db.ChangeTracker.Entries<Challenge>()
            .SingleOrDefault(entry => entry.Entity.Id == template.Id);
        if (trackedTemplate is not null)
        {
            var revision = trackedTemplate.Property(challenge => challenge.Revision);
            revision.OriginalValue = template.Revision;
            revision.CurrentValue = template.Revision;
            revision.IsModified = false;
        }

        var entity = new CompetitionChallenge
        {
            Id = command.CompetitionChallengeId ?? Guid.CreateVersion7(command.CreatedAt),
            CompetitionId = command.CompetitionId,
            ChallengeId = command.ChallengeId,
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
            await LeaderboardDirty.MarkAsync(db, command.CompetitionId, ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return new(Map(entity, template));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(null, ChallengeMutationFailure.RevisionConflict);
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

    public Task<ChallengeView?> FindAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct) =>
        Query(
                includeUnpublished,
                includeDeleted,
                competitionId,
                competitionChallengeId)
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ChallengeView>> ListAsync(
        Guid competitionId,
        bool includeUnpublished,
        bool includeDeleted,
        CancellationToken ct) =>
        await Query(includeUnpublished, includeDeleted, competitionId)
            .ToListAsync(ct);

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
        if (entity.Revision != command.ExpectedRevision)
            return new(null, ChallengeMutationFailure.RevisionConflict);

        var wasPublished = entity.IsPublished;
        var becamePublished = !wasPublished && command.IsPublished;
        entity.BaseScore = command.BaseScore;
        entity.Order = command.Order;
        entity.IsPublished = command.IsPublished;
        entity.Revision = checked(entity.Revision + 1);
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
            await LeaderboardDirty.MarkAsync(db, command.CompetitionId, ct);
            if (becamePublished)
            {
                var publishedTemplate = await db.Challenges.AsNoTracking()
                    .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
                await outbox.PublishAsync(new ChallengePublished(
                    command.CompetitionId,
                    entity.Id,
                    publishedTemplate.Title,
                    publishedTemplate.Direction,
                    command.UpdatedAt,
                    entity.Revision));
            }
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            var template = await db.Challenges.AsNoTracking()
                .SingleAsync(challenge => challenge.Id == entity.ChallengeId, ct);
            return new(Map(entity, template));
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(null, ChallengeMutationFailure.RevisionConflict);
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
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(
            competitionId,
            competitionChallengeId,
            expectedRevision,
            now,
            restore: false,
            ct);

    public Task<ChallengeMutationFailure?> RestoreAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
        DateTimeOffset now,
        CancellationToken ct) =>
        SetDeletedAsync(
            competitionId,
            competitionChallengeId,
            expectedRevision,
            now,
            restore: true,
            ct);

    private async Task<ChallengeMutationFailure?> SetDeletedAsync(
        Guid competitionId,
        Guid competitionChallengeId,
        int expectedRevision,
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
        if (entity.Revision != expectedRevision)
            return ChallengeMutationFailure.RevisionConflict;
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
        entity.Revision = checked(entity.Revision + 1);
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
            await LeaderboardDirty.MarkAsync(db, competitionId, ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return null;
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return ChallengeMutationFailure.RevisionConflict;
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

    private IQueryable<ChallengeView> Query(
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
            .Select(item => new ChallengeView(
                item.Instance.Id,
                item.Instance.CompetitionId,
                item.Instance.ChallengeId,
                item.Template.Title,
                item.Template.Description,
                item.Template.Direction,
                item.Instance.BaseScore,
                item.Instance.Order,
                item.Instance.IsPublished,
                item.Instance.Revision,
                item.Instance.DeletedAt,
                item.Template.CreatedAt,
                item.Instance.UpdatedAt));
    }

    private static ChallengeView Map(CompetitionChallenge instance, Challenge template) =>
        new(
            instance.Id,
            instance.CompetitionId,
            instance.ChallengeId,
            template.Title,
            template.Description,
            template.Direction,
            instance.BaseScore,
            instance.Order,
            instance.IsPublished,
            instance.Revision,
            instance.DeletedAt,
            template.CreatedAt,
            instance.UpdatedAt);

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

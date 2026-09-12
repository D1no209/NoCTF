using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Domain.Runtime;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Competitions.Events;
using NoCTF.Domain.Competitions.Events;

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(
    NoCtfDbContext db,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null,
    ITransactionalMessageOutbox? messageOutbox = null,
    ILogger<AdminCompetitionStore>? logger = null,
    ICompetitionEventRecorder? eventRecorder = null,
    AggregatePatchPostCommitActions? postCommitActions = null)
    : IAdminCompetitionStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new NoOpTransactionalMessageOutbox();
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    public async Task<IReadOnlyList<CompetitionView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = includeDeleted
            ? db.Competitions.IgnoreQueryFilters().AsNoTracking()
            : db.Competitions.AsNoTracking();
        return await Authorized(source, actorId, isAdministrator)
            .OrderByDescending(competition => competition.StartAt)
            .ThenBy(competition => competition.Id)
            .Select(competition => new CompetitionView(
                competition.Id, competition.Title, competition.Description, competition.Mode,
                competition.StartAt, competition.EndAt, competition.Status,
                competition.TeamRegistrationAutoApprove,
                competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.FrozenStartAt, competition.HiddenStartAt,
                competition.AllowTeamRegistrationWhileRunning,
                competition.DeletedAt,
                competition.MaxActiveQuestionsPerTeam,
                competition.MaxParticipantMessagesBeforeHandlerReply,
                competition.AllowChallengeOwnersToHandleQuestions,
                competition.PracticeModeEnabled,
                competition.PosterFileId,
                competition.TracksEnabled,
                competition.AccessMode))
            .ToListAsync(ct);
    }

    public Task<CompetitionView?> FindAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        bool includeDeleted,
        CancellationToken ct)
    {
        var source = includeDeleted
            ? db.Competitions.IgnoreQueryFilters().AsNoTracking()
            : db.Competitions.AsNoTracking();
        return Authorized(source, actorId, isAdministrator)
            .Where(competition => competition.Id == competitionId)
            .Select(competition => new CompetitionView(
                competition.Id, competition.Title, competition.Description, competition.Mode,
                competition.StartAt, competition.EndAt, competition.Status,
                competition.TeamRegistrationAutoApprove,
                competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.FrozenStartAt, competition.HiddenStartAt,
                competition.AllowTeamRegistrationWhileRunning,
                competition.DeletedAt,
                competition.MaxActiveQuestionsPerTeam,
                competition.MaxParticipantMessagesBeforeHandlerReply,
                competition.AllowChallengeOwnersToHandleQuestions,
                competition.PracticeModeEnabled,
                competition.PosterFileId,
                competition.TracksEnabled,
                competition.AccessMode))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CompetitionRestoreResult> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt != null &&
                (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return new(CompetitionRestoreState.NotFound);
        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            entity.ManagerIds.Append(entity.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionRestoreState.UserNotFound,
                eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionRestoreState.RoleNotEligible,
                eligibility.RoleIneligibleUserIds);
        }
        entity.DeletedAt = null;
        entity.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CompetitionRestoreState.NotFound);
        }
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(entity.Id, CancellationToken.None);
        return new(CompetitionRestoreState.Restored);
    }

    public async Task<CompetitionHardDeletePreview?> PreviewHardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var competition = await db.Competitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item =>
                item.Id == competitionId &&
                (isAdministrator || item.OwnerId == actorId))
            .Select(item => new
            {
                item.Id,
                item.Title,
                IsSoftDeleted = item.DeletedAt != null,
                item.Status,
                item.PosterFileId
            })
            .SingleOrDefaultAsync(ct);
        return competition is null
            ? null
            : await BuildHardDeletePreviewAsync(
                competition.Id,
                competition.Title,
                competition.IsSoftDeleted,
                competition.Status,
                isAdministrator,
                competition.PosterFileId,
                ct);
    }

    public async Task<CompetitionHardDeleteResult> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var observed = await db.Competitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId
                && (isAdministrator || competition.OwnerId == actorId))
            .Select(competition => new { competition.DeletedAt })
            .SingleOrDefaultAsync(ct);
        if (observed is null)
            return new(CompetitionHardDeleteState.NotFound);
        await LockForHardDeleteAsync(
            competitionId,
            actorId,
            isAdministrator,
            ct);
        var entity = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId &&
                (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return new(CompetitionHardDeleteState.NotFound);
        if (entity.DeletedAt != observed.DeletedAt)
            return new(CompetitionHardDeleteState.NotFound);
        var preview = await BuildHardDeletePreviewAsync(
            entity.Id,
            entity.Title,
            entity.DeletedAt is not null,
            entity.Status,
            isAdministrator,
            entity.PosterFileId,
            ct);
        if (!preview.CanHardDelete)
            return new(CompetitionHardDeleteState.Blocked, preview);
        var expectedDeletedAt = observed.DeletedAt;
        db.Entry(entity).State = EntityState.Detached;
        var deleted = await db.Competitions
            .IgnoreQueryFilters()
            .Where(competition =>
                competition.Id == competitionId
                && competition.DeletedAt == expectedDeletedAt
                && (isAdministrator || competition.OwnerId == actorId))
            .ExecuteDeleteAsync(ct);
        if (deleted == 0)
            return new(CompetitionHardDeleteState.NotFound);
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(competitionId, CancellationToken.None);
        return new(CompetitionHardDeleteState.Deleted);
    }

    public async Task<CompetitionForceDeleteResult> ForceDeleteAsync(
        ForceDeleteCompetitionCommand command,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!isAdministrator)
            return new(CompetitionForceDeleteState.NotFound);

        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        await LockForHardDeleteAsync(
            command.CompetitionId,
            command.ActorId,
            isAdministrator: true,
            ct);
        var competition = await db.Competitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(item => item.Id == command.CompetitionId)
            .Select(item => new
            {
                item.Id,
                item.Title,
                item.Status,
                item.DeletedAt,
                item.PosterFileId
            })
            .SingleOrDefaultAsync(ct);
        if (competition is null)
            return new(CompetitionForceDeleteState.NotFound);
        if (!string.Equals(
                command.ConfirmationTitle,
                competition.Title,
                StringComparison.Ordinal))
        {
            return new(CompetitionForceDeleteState.ConfirmationMismatch);
        }
        if (competition.Status is CompetitionStatus.Running or CompetitionStatus.Paused)
            return new(CompetitionForceDeleteState.ActiveCompetition);

        var notificationScope = await CompetitionNotificationDeletionScope.LoadAsync(
            db, competition.Id, true, ct);
        var preview = await BuildHardDeletePreviewAsync(
            competition.Id,
            competition.Title,
            competition.DeletedAt is not null,
            competition.Status,
            isAdministrator: true,
            competition.PosterFileId,
            ct,
            notificationScope);
        if (preview.References.Any(reference =>
                reference.Kind == CompetitionHardDeleteReferenceKind.ActiveRuntimeResource))
        {
            return new(CompetitionForceDeleteState.ActiveRuntimeResource, preview);
        }
        if (notificationScope.ConflictingIds.Length > 0)
            return new(CompetitionForceDeleteState.NotificationScopeConflict, preview, notificationScope.ConflictingIds);

        var fileIds = await CollectCompetitionFileIdsAsync(
            command.CompetitionId,
            competition.PosterFileId,
            ct);
        var audit = new Notification
        {
            Id = Guid.CreateVersion7(command.OccurredAt),
            SourceType = NotificationSourceType.User,
            SourceId = command.ActorId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            Kind = NotificationKind.CompetitionForceDeleted,
            ContentJson = JsonSerializer.Serialize(new CompetitionForceDeletionFact(
                SchemaVersion: 1,
                competition.Id,
                competition.Title,
                command.Reason,
                preview.References), JsonOptions),
            SentAt = command.OccurredAt,
            RelatedType = EntityReferenceKind.Competition,
            RelatedId = competition.Id
        };
        await DeleteCompetitionScopeAsync(command.CompetitionId, notificationScope.Ids, ct);
        db.Notifications.Add(audit);
        foreach (var fileId in fileIds)
            await outbox.PublishAsync(new CleanupFile(fileId));
        await outbox.PublishAsync(new InvalidateDeletedCompetitionReadModels(command.CompetitionId));

        // Persist audit and outgoing envelopes in the deletion transaction, not just in memory.
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        try
        {
            await outbox.FlushOutgoingMessagesAsync();
        }
        catch (Exception exception)
        {
            // The deletion has committed. Wolverine's persisted outbox owns delivery retries.
            (logger ?? NullLogger<AdminCompetitionStore>.Instance).LogError(exception,
                "Competition {CompetitionId} was permanently deleted; committed outbox delivery will be retried.",
                command.CompetitionId);
        }
        return new(CompetitionForceDeleteState.Deleted, preview);
    }

    private Task LockForHardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        // PostgreSQL's row lock serializes the impact check with foreign-key inserts.
        // A committed competition event is therefore visible before deletion is decided.
        return isAdministrator
            ? db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 SELECT 1
                 FROM competitions
                 WHERE id = {competitionId}
                 FOR UPDATE
                 """,
                ct)
            : db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 SELECT 1
                 FROM competitions
                 WHERE id = {competitionId} AND owner_id = {actorId}
                 FOR UPDATE
                 """,
                ct);
    }

    public async Task<CompetitionOwnerTransferResult> TransferOwnerAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        Guid ownerId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var entity = await db.Competitions
            .FromSqlInterpolated($"SELECT * FROM competitions WHERE id = {competitionId} FOR UPDATE")
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(ct);
        if (entity is null
            || entity.DeletedAt is not null
            || !(isAdministrator || entity.OwnerId == actorId))
            return new(CompetitionOwnerTransferState.NotFound);
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
                CompetitionOwnerTransferState.UserNotFound,
                UserIds: eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionOwnerTransferState.RoleNotEligible,
                UserIds: eligibility.RoleIneligibleUserIds);
        }
        entity.OwnerId = ownerId;
        entity.ManagerIds = managerIds;
        entity.JudgeIds = entity.JudgeIds
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        entity.ObserverIds = entity.ObserverIds
            .Where(id => id != ownerId)
            .Distinct()
            .Order()
            .ToArray();
        entity.UpdatedAt = now;
        if (previousOwnerId != ownerId)
        {
            await events.RecordAsync(new(
                entity.Id,
                CompetitionEventKind.CompetitionAudienceChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                now,
                ActorUserId: actorId,
                RelatedUserId: ownerId,
                CompetitionStatus: entity.Status,
                CompetitionAccessMode: entity.AccessMode,
                PreviousCompetitionAccessMode: entity.AccessMode,
                CompetitionAudienceChangeKind: CompetitionAudienceChangeKind.Owner), ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await InvalidateReadModelsAsync(entity.Id, CancellationToken.None);
        if (previousOwnerId != ownerId)
            await outbox.FlushOutgoingMessagesAsync();
        return new(CompetitionOwnerTransferState.Transferred, Map(entity));
    }

    private static IQueryable<Competition> Authorized(
        IQueryable<Competition> query,
        Guid actorId,
        bool isAdministrator) =>
        isAdministrator
            ? query
            : query.Where(competition =>
                competition.OwnerId == actorId ||
                competition.ManagerIds.Contains(actorId) ||
                competition.JudgeIds.Contains(actorId) ||
                competition.ObserverIds.Contains(actorId));

    private static CompetitionView Map(Competition competition) =>
        new(
            competition.Id, competition.Title, competition.Description, competition.Mode,
            competition.StartAt, competition.EndAt, competition.Status,
            competition.TeamRegistrationAutoApprove,
            competition.MaxTeamMembers,
            competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
            competition.FrozenStartAt, competition.HiddenStartAt,
            competition.AllowTeamRegistrationWhileRunning,
            competition.DeletedAt,
            PracticeModeEnabled: competition.PracticeModeEnabled,
            AccessMode: competition.AccessMode);

    private Task InvalidateReadModelsAsync(
        Guid competitionId,
        CancellationToken cancellationToken)
    {
        if (readModels is null)
            return Task.CompletedTask;
        return postCommitActions?.RunOrDeferAsync(
                token => readModels.InvalidateAsync(competitionId, token),
                cancellationToken)
            ?? readModels.InvalidateAsync(competitionId, cancellationToken);
    }

    private async Task<CompetitionHardDeletePreview> BuildHardDeletePreviewAsync(
        Guid competitionId,
        string title,
        bool isSoftDeleted,
        CompetitionStatus status,
        bool isAdministrator,
        Guid? posterFileId,
        CancellationToken ct,
        CompetitionNotificationDeletionScope? notificationScope = null)
    {
        var references = new List<CompetitionHardDeleteReference>();
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.HistoricalEvent,
            await db.CompetitionEvents.CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.Team,
            await db.Teams.IgnoreQueryFilters().CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.CompetitionChallenge,
            await db.CompetitionChallenges.IgnoreQueryFilters().CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.GameplayFact,
            await db.GameplayFacts.IgnoreQueryFilters().CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.RuntimeInstance,
            await db.RuntimeInstances.CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.PatchUpload,
            await db.PatchUploads.CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        notificationScope ??= await CompetitionNotificationDeletionScope.LoadAsync(db, competitionId, false, ct);
        AddReference(references, CompetitionHardDeleteReferenceKind.Notification, notificationScope.Ids.Length);
        AddReference(references, CompetitionHardDeleteReferenceKind.NotificationScopeConflict, notificationScope.ConflictingIds.Length);
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.PosterFile,
            posterFileId.HasValue ? 1 : 0);
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.TeamWriteUp,
            await db.Teams.IgnoreQueryFilters().CountAsync(
                item => item.CompetitionId == competitionId
                    && item.WriteUpFileId != null,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.ActiveRuntimeResource,
            await db.RuntimeInstances.CountAsync(item =>
                item.CompetitionId == competitionId
                && (item.State == RuntimeState.Queued
                    || item.State == RuntimeState.Provisioning
                    || item.State == RuntimeState.Running
                    || item.State == RuntimeState.Stopping
                    || item.State == RuntimeState.Failed
                    && (item.ProviderReceiptJson != null
                        || item.RunnerId != null)), ct));
        return new(
            competitionId,
            title,
            isSoftDeleted,
            references.Count == 0,
            isAdministrator
                && status is not CompetitionStatus.Running and not CompetitionStatus.Paused
                && references.All(reference =>
                    reference.Kind is not CompetitionHardDeleteReferenceKind.ActiveRuntimeResource
                        and not CompetitionHardDeleteReferenceKind.NotificationScopeConflict),
            references);
    }

    private async Task<Guid[]> CollectCompetitionFileIdsAsync(
        Guid competitionId,
        Guid? posterFileId,
        CancellationToken ct)
    {
        var ids = new List<Guid>();
        if (posterFileId is Guid poster)
            ids.Add(poster);
        ids.AddRange(await db.Teams.IgnoreQueryFilters()
            .Where(item => item.CompetitionId == competitionId && item.AvatarFileId != null)
            .Select(item => item.AvatarFileId!.Value)
            .ToArrayAsync(ct));
        ids.AddRange(await db.Teams.IgnoreQueryFilters()
            .Where(item => item.CompetitionId == competitionId && item.WriteUpFileId != null)
            .Select(item => item.WriteUpFileId!.Value)
            .ToArrayAsync(ct));
        ids.AddRange(await db.PatchUploads
            .Where(item => item.CompetitionId == competitionId)
            .Select(item => item.FileId)
            .ToArrayAsync(ct));
        return ids.Distinct().ToArray();
    }

    private async Task DeleteCompetitionScopeAsync(
        Guid competitionId,
        Guid[] notificationIds,
        CancellationToken ct)
    {
        await db.Notifications.Where(n => notificationIds.Contains(n.Id)).ExecuteDeleteAsync(ct);
        // Restrict FKs: PatchUpload -> RuntimeInstance -> GameplayFact.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM patch_uploads WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM runtime_instances WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM gameplay_facts WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM challenge_flags WHERE competition_challenge_id IN (SELECT id FROM competition_challenges WHERE competition_id = {competitionId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM competition_events WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM competition_challenges WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM teams WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM competitions WHERE id = {competitionId}", ct);
    }

    private static void AddReference(
        ICollection<CompetitionHardDeleteReference> references,
        CompetitionHardDeleteReferenceKind kind,
        int count)
    {
        if (count > 0)
            references.Add(new(kind, count));
    }

}

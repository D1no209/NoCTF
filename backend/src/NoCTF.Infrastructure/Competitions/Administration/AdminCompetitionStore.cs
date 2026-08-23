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

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(
    NoCtfDbContext db,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null,
    ITransactionalMessageOutbox? messageOutbox = null)
    : IAdminCompetitionStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();
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
                competition.PracticeModeEnabled))
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
                competition.PracticeModeEnabled))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CompetitionRestoreResult> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
            await readModels.InvalidateAsync(entity.Id, ct);
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
            await readModels.InvalidateAsync(competitionId, ct);
        return new(CompetitionHardDeleteState.Deleted);
    }

    public async Task<CompetitionForceDeleteResult> ForceDeleteAsync(
        ForceDeleteCompetitionCommand command,
        bool isAdministrator,
        CancellationToken ct)
    {
        if (!isAdministrator)
            return new(CompetitionForceDeleteState.NotFound);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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

        var preview = await BuildHardDeletePreviewAsync(
            competition.Id,
            competition.Title,
            competition.DeletedAt is not null,
            competition.Status,
            isAdministrator: true,
            competition.PosterFileId,
            ct);
        if (preview.References.Any(reference =>
                reference.Kind == CompetitionHardDeleteReferenceKind.ActiveRuntimeResource))
        {
            return new(CompetitionForceDeleteState.ActiveRuntimeResource, preview);
        }

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
        db.Notifications.Add(audit);
        await db.SaveChangesAsync(ct);

        await DeleteCompetitionScopeAsync(command.CompetitionId, ct);
        foreach (var fileId in fileIds)
            await outbox.PublishAsync(new CleanupFile(fileId));

        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        if (readModels is not null)
            await readModels.InvalidateAsync(command.CompetitionId, ct);
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        if (readModels is not null)
            await readModels.InvalidateAsync(entity.Id, ct);
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
            PracticeModeEnabled: competition.PracticeModeEnabled);

    private async Task<CompetitionHardDeletePreview> BuildHardDeletePreviewAsync(
        Guid competitionId,
        string title,
        bool isSoftDeleted,
        CompetitionStatus status,
        bool isAdministrator,
        Guid? posterFileId,
        CancellationToken ct)
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
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.Notification,
            await db.Notifications.CountAsync(
                item =>
                    (item.RelatedType == EntityReferenceKind.Competition
                        && item.RelatedId == competitionId)
                    || (item.RelatedType == EntityReferenceKind.Team
                        && db.Teams.IgnoreQueryFilters().Any(team =>
                            team.CompetitionId == competitionId
                            && team.Id == item.RelatedId))
                    || (item.RelatedType == EntityReferenceKind.CompetitionChallenge
                        && db.CompetitionChallenges.IgnoreQueryFilters().Any(challenge =>
                            challenge.CompetitionId == competitionId
                            && challenge.Id == item.RelatedId))
                    || (item.RelatedType == EntityReferenceKind.GameplayFact
                        && db.GameplayFacts.IgnoreQueryFilters().Any(fact =>
                            fact.CompetitionId == competitionId
                            && fact.Id == item.RelatedId))
                    || (item.RelatedType == EntityReferenceKind.RuntimeInstance
                        && db.RuntimeInstances.Any(runtime =>
                            runtime.CompetitionId == competitionId
                            && runtime.Id == item.RelatedId))
                    || (item.RelatedType == EntityReferenceKind.CompetitionEvent
                        && db.CompetitionEvents.Any(@event =>
                            @event.CompetitionId == competitionId
                            && @event.Id == item.RelatedId))
                    || (item.SourceType == NotificationSourceType.Competition
                        && item.SourceId == competitionId)
                    || (item.SourceType == NotificationSourceType.Team
                        && db.Teams.IgnoreQueryFilters().Any(team =>
                            team.CompetitionId == competitionId
                            && team.Id == item.SourceId))
                    || ((item.TargetType == NotificationTargetType.CompetitionCollaborators
                            || item.TargetType == NotificationTargetType.CompetitionParticipants)
                        && item.TargetId == competitionId)
                    || (item.TargetType == NotificationTargetType.TeamMembers
                        && db.Teams.IgnoreQueryFilters().Any(team =>
                            team.CompetitionId == competitionId
                            && team.Id == item.TargetId)),
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.PosterFile,
            posterFileId.HasValue ? 1 : 0);
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
                        || item.RunnerId != null
                        || item.RunnerAssignmentReleaseToken != null)), ct));
        return new(
            competitionId,
            title,
            isSoftDeleted,
            references.Count == 0,
            isAdministrator
                && status is not CompetitionStatus.Running and not CompetitionStatus.Paused
                && references.All(reference =>
                    reference.Kind != CompetitionHardDeleteReferenceKind.ActiveRuntimeResource),
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
        ids.AddRange(await db.PatchUploads
            .Where(item => item.CompetitionId == competitionId)
            .Select(item => item.FileId)
            .ToArrayAsync(ct));
        return ids.Distinct().ToArray();
    }

    private async Task DeleteCompetitionScopeAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             WITH RECURSIVE scoped_notifications AS (
                 SELECT id
                 FROM notifications
                 WHERE kind <> {(short)NotificationKind.CompetitionForceDeleted}
                   AND ((related_type = {(short)EntityReferenceKind.Competition} AND related_id = {competitionId})
                    OR (related_type = {(short)EntityReferenceKind.Team} AND related_id IN (SELECT id FROM teams WHERE competition_id = {competitionId}))
                    OR (related_type = {(short)EntityReferenceKind.CompetitionChallenge} AND related_id IN (SELECT id FROM competition_challenges WHERE competition_id = {competitionId}))
                    OR (related_type = {(short)EntityReferenceKind.GameplayFact} AND related_id IN (SELECT id FROM gameplay_facts WHERE competition_id = {competitionId}))
                    OR (related_type = {(short)EntityReferenceKind.RuntimeInstance} AND related_id IN (SELECT id FROM runtime_instances WHERE competition_id = {competitionId}))
                    OR (related_type = {(short)EntityReferenceKind.CompetitionEvent} AND related_id IN (SELECT id FROM competition_events WHERE competition_id = {competitionId}))
                    OR (source_type = {(short)NotificationSourceType.Competition} AND source_id = {competitionId})
                    OR (source_type = {(short)NotificationSourceType.Team} AND source_id IN (SELECT id FROM teams WHERE competition_id = {competitionId}))
                    OR (target_type IN ({(short)NotificationTargetType.CompetitionCollaborators}, {(short)NotificationTargetType.CompetitionParticipants}) AND target_id = {competitionId})
                    OR (target_type = {(short)NotificationTargetType.TeamMembers} AND target_id IN (SELECT id FROM teams WHERE competition_id = {competitionId})))
                 UNION
                 SELECT child.id
                 FROM notifications child
                 INNER JOIN scoped_notifications parent ON child.reply_to_id = parent.id
             )
             DELETE FROM notifications
             WHERE id IN (SELECT id FROM scoped_notifications)
             """, ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM runtime_instances WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM gameplay_facts WHERE competition_id = {competitionId}", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM challenge_flags WHERE competition_challenge_id IN (SELECT id FROM competition_challenges WHERE competition_id = {competitionId}) OR team_id IN (SELECT id FROM teams WHERE competition_id = {competitionId})", ct);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM patch_uploads WHERE competition_id = {competitionId}", ct);
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

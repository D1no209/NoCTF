using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Management;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using NoCTF.Application.Challenges.Images;

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(
    NoCtfDbContext db,
    NoCTF.Infrastructure.Competitions.Management.CompetitionReadModelCache? readModels = null,
    IChallengeImagePinningStore? imagePinning = null,
    IChallengeImageDefinitionCatalog? imageDefinitions = null)
    : IAdminCompetitionStore
{
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
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
                competition.AllowTeamRegistrationWhileRunning,
                competition.DeletedAt))
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
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
                competition.AllowTeamRegistrationWhileRunning,
                competition.DeletedAt))
            .SingleOrDefaultAsync(ct);
    }

    public async Task<CompetitionRestoreResult> RestoreAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        CancellationToken ct) =>
        await RestoreInternalAsync(
            competitionId,
            actorId,
            isAdministrator,
            now,
            expectedChallengeRevisions: null,
            ct);

    public Task<CompetitionRestoreResult> RestoreWithChallengeFenceAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, int> expectedChallengeRevisions,
        CancellationToken ct) =>
        RestoreInternalAsync(
            competitionId,
            actorId,
            isAdministrator,
            now,
            expectedChallengeRevisions,
            ct);

    private async Task<CompetitionRestoreResult> RestoreInternalAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        DateTimeOffset now,
        IReadOnlyDictionary<Guid, int>? expectedChallengeRevisions,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (imagePinning is not null && imageDefinitions is not null)
        {
            var boundary = await imagePinning.LockCompetitionPublicationBoundaryAsync(
                competitionId,
                includeDeleted: true,
                ct);
            if (!boundary.CompetitionExists)
                return new(CompetitionRestoreState.NotFound);
            if (expectedChallengeRevisions is not null
                && (boundary.Challenges.Count != expectedChallengeRevisions.Count
                    || boundary.Challenges.Any(challenge =>
                        !expectedChallengeRevisions.TryGetValue(
                            challenge.ChallengeId,
                            out var revision)
                        || challenge.Revision != revision)))
            {
                return new(
                    CompetitionRestoreState.ChallengeDefinitionRevisionConflict,
                    Detail: "Challenge definition changed after its images were resolved.");
            }
            if (boundary.Challenges.Any(challenge =>
            {
                var definition = imageDefinitions.Read(
                    challenge.Mode,
                    challenge.DefinitionJson);
                return !definition.Succeeded
                    || definition.Images!.Any(image =>
                        !ContainerImageReference.TryParse(image.Image, out var parsed)
                        || !parsed.IsDigest);
            }))
            {
                return new(
                    CompetitionRestoreState.ChallengeImageInvalid,
                    Detail: "Every Runtime and Checker image must use a sha256 digest before restoring this competition.");
            }
        }
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
                item.PosterFileId
            })
            .SingleOrDefaultAsync(ct);
        return competition is null
            ? null
            : await BuildHardDeletePreviewAsync(
                competition.Id,
                competition.Title,
                competition.IsSoftDeleted,
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
        if (await CompetitionStateReader.ReadAsync(db, competitionId, ct) is null)
            return new(CompetitionOwnerTransferState.NotFound);
        var entity = await db.Competitions.SingleOrDefaultAsync(competition =>
            competition.Id == competitionId &&
            (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
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
        entity.PermissionRevision = checked(entity.PermissionRevision + 1);
        entity.UpdatedAt = now;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return new(CompetitionOwnerTransferState.RevisionConflict);
        }
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
            competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt,
            competition.AllowTeamRegistrationWhileRunning,
            competition.DeletedAt);

    private async Task<CompetitionHardDeletePreview> BuildHardDeletePreviewAsync(
        Guid competitionId,
        string title,
        bool isSoftDeleted,
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
            CompetitionHardDeleteReferenceKind.DataExport,
            await db.DataExports.CountAsync(
                item => item.CompetitionId == competitionId,
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.Notification,
            await db.Notifications.CountAsync(
                item =>
                    (item.RelatedType == EntityReferenceKind.Competition
                        && item.RelatedId == competitionId)
                    || (item.SourceType == NotificationSourceType.Competition
                        && item.SourceId == competitionId)
                    || ((item.TargetType == NotificationTargetType.CompetitionCollaborators
                            || item.TargetType == NotificationTargetType.CompetitionParticipants)
                        && item.TargetId == competitionId),
                ct));
        AddReference(
            references,
            CompetitionHardDeleteReferenceKind.PosterFile,
            posterFileId.HasValue ? 1 : 0);
        return new(
            competitionId,
            title,
            isSoftDeleted,
            references.Count == 0,
            references);
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

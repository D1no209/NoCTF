using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NoCTF.Application.Competitions.Management;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Challenges;
using NoCTF.Domain.Challenges.Questions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Runtime;
using NoCTF.Infrastructure.Administration;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Competitions.Administration;

public sealed class AdminCompetitionStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox? messageOutbox = null,
    ILogger<AdminCompetitionStore>? logger = null) : IAdminCompetitionStore
{
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();
    private readonly ILogger<AdminCompetitionStore> log =
        logger ?? NullLogger<AdminCompetitionStore>.Instance;

    public async Task<IReadOnlyList<CompetitionView>> ListAsync(
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct) =>
        await Authorized(db.Competitions.AsNoTracking(), actorId, isAdministrator)
            .OrderByDescending(competition => competition.StartAt)
            .ThenBy(competition => competition.Id)
            .Select(competition => new CompetitionView(
                competition.Id, competition.Title, competition.Description, competition.Mode,
                competition.StartAt, competition.EndAt, competition.Status,
                competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt))
            .ToListAsync(ct);

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
                competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers,
                competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
                competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt))
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
        await CompetitionWriteLock.AcquireTransactionLockAsync(db, competitionId, ct);
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
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(CompetitionRestoreState.Restored);
    }

    public async Task<bool> HardDeleteAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompetitionWriteLock.AcquireTransactionLockAsync(db, competitionId, ct);
        var entity = await db.Competitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(competition =>
                competition.Id == competitionId &&
                competition.Status == CompetitionStatus.Finished &&
                (isAdministrator || competition.OwnerId == actorId), ct);
        if (entity is null)
            return false;

        var hasActiveRuntime = await db.RuntimeInstances.AnyAsync(runtime =>
            runtime.CompetitionId == competitionId &&
            (runtime.State == RuntimeState.Queued ||
             runtime.State == RuntimeState.Provisioning ||
             runtime.State == RuntimeState.Running ||
             runtime.State == RuntimeState.Stopping), ct);
        if (hasActiveRuntime)
            return false;

        if (!await DeleteCompetitionEventsAsync(competitionId, ct) ||
            !await DeleteCompetitionQuestionEntriesAsync(competitionId, ct))
            return false;

        var patchObjectKeys = db.PatchUploads
            .Where(upload => upload.CompetitionId == competitionId)
            .Select(upload => upload.ObjectKey);
        var exportObjectKeys = db.DataExports
            .Where(item => item.CompetitionId == competitionId && item.ObjectKey != null)
            .Select(item => item.ObjectKey!);
        var objectKeys = await patchObjectKeys
            .Concat(exportObjectKeys)
            .Distinct()
            .ToListAsync(ct);
        foreach (var objectKey in objectKeys)
            await outbox.PublishAsync(new CleanupObject(objectKey));

        await db.Notifications
            .Where(notification => notification.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.DataExports
            .Where(item => item.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.CompetitionQuestions
            .Where(question => question.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);

        await db.RuntimeInstances
            .Where(runtime => runtime.CompetitionId == competitionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    runtime => runtime.ReplacesRuntimeInstanceId,
                    (Guid?)null),
                ct);
        await db.RuntimeInstances
            .Where(runtime => runtime.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);

        await db.Submissions
            .Where(submission => submission.CompetitionId == competitionId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    submission => submission.CurrentScoringEventId,
                    (Guid?)null),
                ct);
        await db.ScoringEvents.IgnoreQueryFilters()
            .Where(item => item.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.Submissions
            .Where(submission => submission.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.PatchUploads
            .Where(upload => upload.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);

        var competitionChallengeIds = db.CompetitionChallenges.IgnoreQueryFilters()
            .Where(challenge => challenge.CompetitionId == competitionId)
            .Select(challenge => challenge.Id);
        await db.ChallengeFlags.IgnoreQueryFilters()
            .Where(flag => flag.CompetitionChallengeId != null &&
                competitionChallengeIds.Contains(flag.CompetitionChallengeId.Value))
            .ExecuteDeleteAsync(ct);
        await db.Set<CompetitionChallengeHint>()
            .Where(hint => competitionChallengeIds.Contains(hint.CompetitionChallengeId))
            .ExecuteDeleteAsync(ct);
        await db.Teams.IgnoreQueryFilters()
            .Where(team => team.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.CompetitionChallenges.IgnoreQueryFilters()
            .Where(challenge => challenge.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.Set<CompetitionLifecycleAudit>()
            .Where(audit => audit.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);
        await db.Set<CompetitionLeaderboardVisibilityAudit>()
            .Where(audit => audit.CompetitionId == competitionId)
            .ExecuteDeleteAsync(ct);

        db.Competitions.Remove(entity);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        try
        {
            await outbox.FlushOutgoingMessagesAsync();
        }
        catch (Exception exception)
        {
            log.LogWarning(
                exception,
                "Competition {CompetitionId} was permanently deleted, but object cleanup messages were not flushed immediately.",
                competitionId);
        }
        return true;
    }

    private async Task<bool> DeleteCompetitionEventsAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var competitionEvents = db.CompetitionEvents
            .Where(item => item.CompetitionId == competitionId);
        while (await competitionEvents.AnyAsync(ct))
        {
            var deleted = await competitionEvents
                .Where(item => !db.CompetitionEvents.Any(child =>
                    child.ParentEventId == item.Id))
                .ExecuteDeleteAsync(ct);
            if (deleted == 0)
                return false;
        }
        return true;
    }

    private async Task<bool> DeleteCompetitionQuestionEntriesAsync(
        Guid competitionId,
        CancellationToken ct)
    {
        var entries = db.Set<CompetitionQuestionEntry>()
            .Where(entry => db.CompetitionQuestions.Any(question =>
                question.Id == entry.QuestionId &&
                question.CompetitionId == competitionId));
        while (await entries.AnyAsync(ct))
        {
            var deleted = await entries
                .Where(entry => !db.Set<CompetitionQuestionEntry>().Any(child =>
                    child.TargetEntryId == entry.Id))
                .ExecuteDeleteAsync(ct);
            if (deleted == 0)
                return false;
        }
        return true;
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
        if (await CompetitionWriteLock.AcquireAsync(db, competitionId, ct) is null)
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
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
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
            competition.TeamRegistrationAutoApprove, competition.MaxTeamMembers,
            competition.MaxConcurrentRuntimeInstancesPerTeam, competition.OwnerId,
            competition.LeaderboardVisibility, competition.LeaderboardVisibilityStartsAt);

}

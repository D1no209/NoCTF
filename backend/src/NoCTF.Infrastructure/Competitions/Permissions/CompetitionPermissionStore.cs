using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Competitions.Permissions;
using NoCTF.Domain.Competitions;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Administration;
using NoCTF.Application.Competitions.Events;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Competitions.Events;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Competitions.Permissions;

public sealed class CompetitionPermissionStore(
    NoCtfDbContext db,
    TimeProvider? clock = null,
    IPostCommitMessagePublisher? messageOutbox = null,
    ICompetitionEventRecorder? eventRecorder = null) : ICompetitionPermissionStore
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly IPostCommitMessagePublisher outbox =
        messageOutbox ?? new NoOpPostCommitMessagePublisher();
    private readonly ICompetitionEventRecorder events =
        eventRecorder ?? NullCompetitionEventRecorder.Instance;
    public async Task<CompetitionPermissionSnapshotResult> GetSnapshotAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var snapshot = await db.Competitions.AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt == null)
            .Select(competition => new CompetitionPermissionSnapshot(
                competition.Id,
                competition.OwnerId,
                competition.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Manager)
                    .Select(item => item.UserId).ToArray(),
                competition.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Judge)
                    .Select(item => item.UserId).ToArray(),
                competition.Collaborators.Where(item =>
                        item.Role == CompetitionCollaboratorRole.Observer)
                    .Select(item => item.UserId).ToArray()))
            .SingleOrDefaultAsync(ct);
        if (snapshot is null)
            return new(CompetitionPermissionSnapshotState.NotFound);
        if (!isAdministrator && snapshot.OwnerId != actorId)
            return new(CompetitionPermissionSnapshotState.Forbidden);

        return new(
            CompetitionPermissionSnapshotState.Found,
            snapshot with
            {
                ManagerIds = snapshot.ManagerIds.Distinct().Order().ToArray(),
                JudgeIds = snapshot.JudgeIds.Distinct().Order().ToArray(),
                ObserverIds = snapshot.ObserverIds.Distinct().Order().ToArray()
            });
    }

    public async Task<CompetitionPermissionCandidateListResult> ListCandidatesAsync(
        Guid competitionId,
        Guid actorId,
        bool isAdministrator,
        CancellationToken ct)
    {
        var ownerId = await db.Competitions.AsNoTracking()
            .Where(competition =>
                competition.Id == competitionId &&
                competition.DeletedAt == null)
            .Select(competition => (Guid?)competition.OwnerId)
            .SingleOrDefaultAsync(ct);
        if (ownerId is null)
            return new(CompetitionPermissionCandidateListState.NotFound);
        if (!isAdministrator && ownerId.Value != actorId)
            return new(CompetitionPermissionCandidateListState.Forbidden);

        var candidates = await db.Users.AsNoTracking()
            .Where(user => user.Id != ownerId.Value)
            .OrderBy(user => user.UserName)
            .ThenBy(user => user.Id)
            .Select(user => new CompetitionPermissionCandidate(
                user.Id,
                user.UserName,
                user.Kind,
                user.Role,
                user.EmailVerifiedAt != null))
            .ToArrayAsync(ct);
        return new(
            CompetitionPermissionCandidateListState.Listed,
            candidates);
    }

    public async Task<CompetitionPermissionUpdateResult> UpdateAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await UpdateOnceAsync(command, ct);
            }
            catch (Exception exception) when (attempt < 2
                && exception is not DbUpdateConcurrencyException
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<CompetitionPermissionUpdateResult> UpdateOnceAsync(
        UpdateCompetitionPermissionsCommand command,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db,
            System.Data.IsolationLevel.Serializable,
            ct);
        await CompetitionStateReader.ReadAsync(db, command.CompetitionId, ct);
        var competition = await db.Competitions
            .Include(item => item.Collaborators)
            .SingleOrDefaultAsync(
            item => item.Id == command.CompetitionId && item.DeletedAt == null,
            ct);
        if (competition is null)
            return new(CompetitionPermissionUpdateState.NotFound);

        var administrator = await db.Users.AsNoTracking().AnyAsync(
            user => user.Id == command.ActorId && user.Role == UserRole.Administrator,
            ct);
        if (competition.OwnerId != command.ActorId && !administrator)
            return new(CompetitionPermissionUpdateState.Forbidden);
        var userIds = command.ManagerIds
            .Concat(command.JudgeIds)
            .Concat(command.ObserverIds)
            .ToArray();
        if (userIds.Length != userIds.Distinct().Count())
            return new(CompetitionPermissionUpdateState.RolesOverlap);
        if (userIds.Contains(competition.OwnerId))
        {
            return new(
                CompetitionPermissionUpdateState.OwnerIncluded,
                [competition.OwnerId]);
        }

        var eligibility = await ResourceManagerRoleGuard.AcquireAndCheckAsync(
            db,
            command.ManagerIds.Append(competition.OwnerId),
            ct);
        if (eligibility.MissingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                eligibility.MissingUserIds);
        }
        if (eligibility.RoleIneligibleUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.RoleNotEligible,
                eligibility.RoleIneligibleUserIds);
        }

        var judgeObserverIds = command.JudgeIds
            .Concat(command.ObserverIds)
            .Distinct()
            .Order()
            .ToArray();
        var judgeObserverUserIds = await db.Users.AsNoTracking()
            .Where(user => judgeObserverIds.Contains(user.Id))
            .Select(user => user.Id)
            .ToArrayAsync(ct);
        var missingUserIds = judgeObserverIds
            .Except(judgeObserverUserIds)
            .ToArray();
        if (missingUserIds.Length > 0)
        {
            return new(
                CompetitionPermissionUpdateState.UserNotFound,
                missingUserIds);
        }
        var managerIds = command.ManagerIds.Distinct().Order().ToArray();
        var judgeIds = command.JudgeIds.Distinct().Order().ToArray();
        var observerIds = command.ObserverIds.Distinct().Order().ToArray();
        var audienceChanged = !competition.ManagerIds.SequenceEqual(managerIds)
            || !competition.JudgeIds.SequenceEqual(judgeIds)
            || !competition.ObserverIds.SequenceEqual(observerIds);
        competition.ManagerIds = managerIds;
        competition.JudgeIds = judgeIds;
        competition.ObserverIds = observerIds;
        competition.UpdatedAt = timeProvider.GetUtcNow();
        if (audienceChanged)
        {
            await events.RecordAsync(new(
                competition.Id,
                CompetitionEventKind.CompetitionAudienceChanged,
                CompetitionEventLevel.Information,
                CompetitionEventVisibility.Staff,
                competition.UpdatedAt,
                ActorUserId: command.ActorId,
                CompetitionStatus: competition.Status,
                CompetitionAccessMode: competition.AccessMode,
                PreviousCompetitionAccessMode: competition.AccessMode,
                CompetitionAudienceChangeKind: CompetitionAudienceChangeKind.Collaborators), ct);
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(ct);
            return new(CompetitionPermissionUpdateState.Conflict);
        }
        await transaction.CommitAsync(ct);
        if (audienceChanged)
            await transaction.FlushMessagesAsync(outbox);
        return new(CompetitionPermissionUpdateState.Updated);
    }
}

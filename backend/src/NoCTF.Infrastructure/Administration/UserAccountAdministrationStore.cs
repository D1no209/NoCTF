using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Application.Scoring.Leaderboard;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;

namespace NoCTF.Infrastructure.Administration;

public sealed class UserAccountAdministrationStore(
    NoCtfDbContext db,
    ITransactionalMessageOutbox? messageOutbox = null)
    : IUserAccountAdministrationStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new NoOpTransactionalMessageOutbox();
    public async Task<UserDeletionPreview?> PreviewDeletionAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.Id == userId,
            ct);
        return user is null ? null : await BuildPreviewAsync(user, actorUserId, ct);
    }

    public async Task<UserDeletionStoreResult> DeleteAsync(
        Guid userId,
        Guid actorUserId,
        UserDeletionMode mode,
        string reason,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        await ActiveHumanAdministratorMutationGuard.AcquireAsync(db, ct);
        await ResourceManagerRoleGuard.AcquireAsync(db, [userId], ct);
        var user = await db.Users.Include(candidate => candidate.AvatarFile)
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
        if (user is null)
            return new(UserDeletionState.UserNotFound);

        var preview = await BuildPreviewAsync(user, actorUserId, ct);
        if (preview.SelfDeletionForbidden)
            return new(UserDeletionState.SelfDeletionForbidden, preview);
        if (preview.LastAdministratorProtected)
            return new(UserDeletionState.LastAdministratorProtected, preview);

        if (mode == UserDeletionMode.HardDelete && !preview.CanHardDelete)
            return new(UserDeletionState.HardDeleteBlocked, preview);
        if (mode == UserDeletionMode.Anonymize && user.AccountStatus == UserAccountStatus.Anonymized)
            return new(UserDeletionState.AlreadyAnonymized, preview);
        if (mode == UserDeletionMode.Anonymize && !preview.CanAnonymize)
            return new(UserDeletionState.HardDeleteBlocked, preview);

        var previousAvatarFileId = user.AvatarFileId;
        var originalUserName = user.UserName;
        await db.AccountTokens
            .Where(token => token.UserId == userId)
            .ExecuteDeleteAsync(ct);

        if (mode == UserDeletionMode.HardDelete)
        {
            RecordLifecycleFact(
                user,
                actorUserId,
                UserAccountLifecycleAction.PhysicallyDeleted,
                reason,
                now);
            db.Users.Remove(user);
            if (previousAvatarFileId is { } previous)
                await outbox.PublishAsync(new CleanupFile(previous));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await outbox.FlushOutgoingMessagesAsync();
            return new(UserDeletionState.PhysicallyDeleted, preview, previousAvatarFileId);
        }

        await RemoveResourcePermissionsAsync(userId, ct);
        user.UserName = $"anonymous-{user.Id:N}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = string.Empty;
        user.PasswordHash = string.Empty;
        user.Role = UserRole.User;
        user.AccountStatus = UserAccountStatus.Anonymized;
        await NoCTF.Infrastructure.Authentication.UserCredentialWrite.InvalidateTokensAsync(db, user, ct);
        user.EmailVerifiedAt = null;
        user.Description = null;
        user.SchoolFullName = null;
        user.SchoolStudentNumber = null;
        user.AvatarFileId = null;
        user.AvatarFile = null;
        user.UpdatedAt = now;
        await AnonymizeLeaderboardProjectionsAsync(
            userId,
            originalUserName,
            user.UserName,
            ct);
        RecordLifecycleFact(
            user,
            actorUserId,
            UserAccountLifecycleAction.Anonymized,
            reason,
            now,
            originalUserName);
        if (previousAvatarFileId is { } previousFileId)
            await outbox.PublishAsync(new CleanupFile(previousFileId));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new(UserDeletionState.Anonymized, preview, previousAvatarFileId);
    }

    private void RecordLifecycleFact(
        User user,
        Guid actorUserId,
        UserAccountLifecycleAction action,
        string reason,
        DateTimeOffset occurredAt,
        string? targetUserName = null)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.CreateVersion7(occurredAt),
            SourceType = NotificationSourceType.User,
            SourceId = actorUserId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            Kind = NotificationKind.UserAccountLifecycleChanged,
            ContentJson = JsonSerializer.Serialize(new UserAccountLifecycleFact(
                1,
                user.Id,
                targetUserName ?? user.UserName,
                action,
                reason,
                false), JsonOptions),
            RelatedType = EntityReferenceKind.User,
            RelatedId = user.Id,
            SentAt = occurredAt
        });
    }

    private async Task<UserDeletionPreview> BuildPreviewAsync(
        User user,
        Guid actorUserId,
        CancellationToken ct)
    {
        var teamIds = await db.Teams.IgnoreQueryFilters().AsNoTracking()
            .Where(team => team.MemberIds.Contains(user.Id))
            .Select(team => team.Id)
            .ToArrayAsync(ct);
        var references = new List<UserDeletionReference>();
        await AddReferenceAsync(
            UserDeletionReferenceKind.CompetitionOwner,
            db.Competitions.IgnoreQueryFilters().CountAsync(
                competition => competition.OwnerId == user.Id,
                ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.CompetitionCollaborator,
            db.Competitions.IgnoreQueryFilters().CountAsync(
                competition => competition.ManagerIds.Contains(user.Id)
                    || competition.JudgeIds.Contains(user.Id)
                    || competition.ObserverIds.Contains(user.Id),
                ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.ChallengeOwner,
            db.Challenges.IgnoreQueryFilters().CountAsync(
                challenge => challenge.OwnerId == user.Id,
                ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.ChallengeManager,
            db.Challenges.IgnoreQueryFilters().CountAsync(
                challenge => challenge.ManagerIds.Contains(user.Id),
                ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.TeamCaptain,
            db.Teams.IgnoreQueryFilters().CountAsync(team => team.CaptainId == user.Id, ct));
        AddReference(UserDeletionReferenceKind.TeamMember, teamIds.Length);
        await AddReferenceAsync(
            UserDeletionReferenceKind.GameplayFact,
            db.GameplayFacts.CountAsync(submission => submission.ActorUserId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.PatchUpload,
            db.PatchUploads.CountAsync(upload => upload.UploadedByUserId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.Notification,
            db.Notifications.CountAsync(notification =>
                notification.SourceType == NotificationSourceType.User
                    && notification.SourceId == user.Id
                || (notification.TargetType == NotificationTargetType.User
                    && notification.TargetId == user.Id)
                || (notification.RelatedType == EntityReferenceKind.User
                    && notification.RelatedId == user.Id), ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.CompetitionEvent,
            db.CompetitionEvents.CountAsync(entry =>
                entry.ActorUserId == user.Id
                || entry.RelatedType == EntityReferenceKind.User
                    && entry.RelatedId == user.Id,
                ct));
        var selfDeletionForbidden = user.Id == actorUserId;
        var lastAdministratorProtected = ActiveHumanAdministratorMutationGuard.Contains(user)
            && await ActiveHumanAdministratorMutationGuard.CountAsync(db, ct) <= 1;
        var hasReferences = references.Count > 0;
        return new(
            user.Id,
            user.UserName,
            user.AccountStatus,
            CanHardDelete: !selfDeletionForbidden && !lastAdministratorProtected && !hasReferences,
            CanAnonymize: !selfDeletionForbidden
                && !lastAdministratorProtected
                && user.AccountStatus != UserAccountStatus.Anonymized,
            selfDeletionForbidden,
            lastAdministratorProtected,
            references);

        async Task AddReferenceAsync(UserDeletionReferenceKind kind, Task<int> countTask) =>
            AddReference(kind, await countTask);

        void AddReference(UserDeletionReferenceKind kind, int count)
        {
            if (count > 0)
                references.Add(new(kind, count));
        }
    }

    private async Task RemoveResourcePermissionsAsync(Guid userId, CancellationToken ct)
    {
        var competitions = await db.Competitions.IgnoreQueryFilters()
            .Where(competition => competition.ManagerIds.Contains(userId)
                || competition.JudgeIds.Contains(userId)
                || competition.ObserverIds.Contains(userId))
            .ToListAsync(ct);
        foreach (var competition in competitions)
        {
            competition.ManagerIds = competition.ManagerIds.Where(id => id != userId).ToArray();
            competition.JudgeIds = competition.JudgeIds.Where(id => id != userId).ToArray();
            competition.ObserverIds = competition.ObserverIds.Where(id => id != userId).ToArray();
        }

        var challenges = await db.Challenges.IgnoreQueryFilters()
            .Where(challenge => challenge.ManagerIds.Contains(userId))
            .ToListAsync(ct);
        foreach (var challenge in challenges)
            challenge.ManagerIds = challenge.ManagerIds.Where(id => id != userId).ToArray();
    }

    private async Task AnonymizeLeaderboardProjectionsAsync(
        Guid userId,
        string originalUserName,
        string anonymousUserName,
        CancellationToken ct)
    {
        // Leaderboards are projections over PostgreSQL facts. No identity-bearing
        // snapshot is persisted on Competition, so the next projection observes the
        // anonymized User directly.
        await Task.CompletedTask;
    }

}

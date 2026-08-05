using System.Data;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public sealed class UserAccountAdministrationStore(NoCtfDbContext db)
    : IUserAccountAdministrationStore
{
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
        await ResourceManagerRoleGuard.AcquireAsync(db, [userId], ct);
        var user = await db.Users.SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
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

        var avatarObjectKey = user.AvatarObjectKey;
        var originalUserName = user.UserName;
        await db.EmailVerificationTokens
            .Where(token => token.UserId == userId)
            .ExecuteDeleteAsync(ct);

        if (mode == UserDeletionMode.HardDelete)
        {
            db.Users.Remove(user);
            AddAudit(
                userId,
                originalUserName,
                actorUserId,
                UserAccountLifecycleAction.PhysicallyDeleted,
                reason,
                now);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(UserDeletionState.PhysicallyDeleted, preview, avatarObjectKey);
        }

        await RemoveResourcePermissionsAsync(userId, ct);
        user.UserName = $"anonymous-{user.Id:N}";
        user.NormalizedUserName = user.UserName.ToUpperInvariant();
        user.Email = string.Empty;
        user.NormalizedEmail = string.Empty;
        user.PasswordHash = string.Empty;
        user.Role = UserRole.User;
        user.AccountStatus = UserAccountStatus.Anonymized;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.EmailVerifiedAt = null;
        user.Description = null;
        user.AvatarObjectKey = null;
        user.IsEmailPublic = false;
        user.UpdatedAt = now;
        AddAudit(
            userId,
            originalUserName,
            actorUserId,
            UserAccountLifecycleAction.Anonymized,
            reason,
            now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(UserDeletionState.Anonymized, preview, avatarObjectKey);
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
            UserDeletionReferenceKind.Submission,
            db.Submissions.CountAsync(submission => submission.SubmittedByUserId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.PatchUpload,
            db.PatchUploads.CountAsync(upload => upload.UploadedByUserId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.Notification,
            db.Notifications.CountAsync(notification => notification.UserId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.ScoringEvent,
            db.ScoringEvents.CountAsync(scoringEvent =>
                scoringEvent.TeamId != null && teamIds.Contains(scoringEvent.TeamId.Value)
                || scoringEvent.VictimTeamId != null
                && teamIds.Contains(scoringEvent.VictimTeamId.Value),
                ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.CompetitionLifecycleAudit,
            db.Set<NoCTF.Domain.Competitions.CompetitionLifecycleAudit>()
                .CountAsync(audit => audit.ActorId == user.Id, ct));
        await AddReferenceAsync(
            UserDeletionReferenceKind.UserAccountLifecycleAudit,
            db.UserAccountLifecycleAudits.CountAsync(
                audit => audit.TargetUserId == user.Id || audit.ActorUserId == user.Id,
                ct));

        var selfDeletionForbidden = user.Id == actorUserId;
        var lastAdministratorProtected = user.Role == UserRole.Administrator
            && user.AccountStatus == UserAccountStatus.Active
            && await db.Users.CountAsync(candidate =>
                candidate.Role == UserRole.Administrator
                && candidate.AccountStatus == UserAccountStatus.Active,
                ct) <= 1;
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

    private void AddAudit(
        Guid targetUserId,
        string targetUserName,
        Guid actorUserId,
        UserAccountLifecycleAction action,
        string reason,
        DateTimeOffset now) =>
        db.UserAccountLifecycleAudits.Add(new UserAccountLifecycleAudit
        {
            Id = Guid.CreateVersion7(now),
            TargetUserId = targetUserId,
            TargetUserName = targetUserName,
            ActorUserId = actorUserId,
            Action = action,
            Reason = reason,
            OccurredAt = now
        });
}

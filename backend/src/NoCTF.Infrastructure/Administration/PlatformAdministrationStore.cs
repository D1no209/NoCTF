using System.Text.Json;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAdministrationStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher) : IPlatformAdministrationStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<PlatformUserView>> ListUsersAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking()
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.Id)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
                user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.ProviderId,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Protocol,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Subject,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.BoundAt))
            .ToListAsync(ct);

    public async Task<PlatformUserListPage> ListUsersPageAsync(
        PlatformUserListQuery query,
        CancellationToken ct)
    {
        var source = db.Users.AsNoTracking();
        var keyword = string.IsNullOrWhiteSpace(query.Keyword) ? null : query.Keyword.Trim();
        if (keyword is not null)
        {
            var normalized = keyword.ToUpperInvariant();
            source = source.Where(user =>
                user.NormalizedUserName.Contains(normalized)
                || user.NormalizedEmail.Contains(normalized)
                || user.ExternalIdentity != null
                    && user.ExternalIdentity.NormalizedSubject.Contains(normalized));
        }
        if (query.Kind is not null)
            source = source.Where(user => user.Kind == query.Kind);
        if (query.Role is not null)
            source = source.Where(user => user.Role == query.Role);
        if (query.SsoProviderId is not null)
            source = source.Where(user =>
                user.ExternalIdentity != null
                && user.ExternalIdentity.ProviderId == query.SsoProviderId);

        var total = await source.CountAsync(ct);
        var ordered = query.Desc
            ? source.OrderByDescending(user => user.CreatedAt).ThenByDescending(user => user.Id)
            : source.OrderBy(user => user.CreatedAt).ThenBy(user => user.Id);
        var items = await ordered
            .Skip(query.Offset)
            .Take(query.Limit)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
                user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.ProviderId,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Protocol,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Subject,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.BoundAt))
            .ToListAsync(ct);
        return new(items, total);
    }

    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
                user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.ProviderId,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Protocol,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.Subject,
                user.ExternalIdentity == null ? null : user.ExternalIdentity.BoundAt))
            .SingleOrDefaultAsync(ct);

    public async Task<CreateBotResult> CreateBotAsync(
        string userName,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var trimmedUserName = userName.Trim();
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return new(CreateBotState.UserNameConflict);

        var id = Guid.CreateVersion7(now);
        var email = EmailCanonicalizer.Canonicalize(BotIdentity.DummyEmail(id));
        var user = new User
        {
            Id = id,
            UserName = trimmedUserName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            Kind = UserKind.Bot,
            Role = role,
            AccountStatus = UserAccountStatus.Active,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, Guid.NewGuid().ToString("N"));
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return new(CreateBotState.Created, Map(user));
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return new(CreateBotState.UserNameConflict);
        }
    }

    public async Task<UpdatePlatformRoleResult> UpdateRoleAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await UpdateRoleOnceAsync(userId, role, now, ct);
            }
            catch (Exception exception) when (attempt < 2
                && RelationalRetry.IsTransientConcurrency(exception))
            {
                db.ChangeTracker.Clear();
                await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(5, 31)), ct);
            }
        }
    }

    private async Task<UpdatePlatformRoleResult> UpdateRoleOnceAsync(
        Guid userId,
        UserRole role,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(
            db, System.Data.IsolationLevel.Serializable, ct);
        await ActiveHumanAdministratorMutationGuard.AcquireAsync(db, ct);
        await ResourceManagerRoleGuard.AcquireAsync(db, [userId], ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return new(UpdatePlatformRoleState.UserNotFound);
        var removesActiveAdministrator = ActiveHumanAdministratorMutationGuard.Contains(user)
            && role != UserRole.Administrator;
        if (removesActiveAdministrator
            && await ActiveHumanAdministratorMutationGuard.CountAsync(db, ct) <= 1)
        {
            return new(UpdatePlatformRoleState.LastAdministratorProtected);
        }
        if (role == UserRole.User && user.Role.CanManageResources())
        {
            var competitionIds = await db.Competitions.AsNoTracking()
                .Where(competition =>
                    competition.OwnerId == userId ||
                    competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId))
                .OrderBy(competition => competition.Id)
                .Select(competition => competition.Id)
                .ToArrayAsync(ct);
            var challengeIds = await db.Challenges.AsNoTracking()
                .Where(challenge =>
                    challenge.OwnerId == userId ||
                    challenge.Managers.Any(manager => manager.UserId == userId))
                .OrderBy(challenge => challenge.Id)
                .Select(challenge => challenge.Id)
                .ToArrayAsync(ct);
            if (competitionIds.Length > 0 || challengeIds.Length > 0)
            {
                return new(
                    UpdatePlatformRoleState.ActiveOwnerOrManagerAssignments,
                    Blockers: new(competitionIds, challengeIds));
            }
        }
        user.Role = role;
        await NoCTF.Infrastructure.Authentication.UserCredentialWrite.InvalidateTokensAsync(db, user, ct);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(UpdatePlatformRoleState.Updated, Map(user));
    }

    public async Task<UpdatePlatformUserStatusResult> UpdateAccountStatusAsync(
        Guid userId,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        await ActiveHumanAdministratorMutationGuard.AcquireAsync(db, ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return new(UpdatePlatformUserStatusState.UserNotFound);
        if (user.AccountStatus == UserAccountStatus.Anonymized
            || accountStatus == UserAccountStatus.Anonymized)
        {
            return new(UpdatePlatformUserStatusState.AnonymizedAccountImmutable);
        }
        if (user.AccountStatus == accountStatus)
            return new(UpdatePlatformUserStatusState.Updated, Map(user));
        if (ActiveHumanAdministratorMutationGuard.Contains(user)
            && accountStatus != UserAccountStatus.Active
            && await ActiveHumanAdministratorMutationGuard.CountAsync(db, ct) <= 1)
        {
            return new(UpdatePlatformUserStatusState.LastAdministratorProtected);
        }

        user.AccountStatus = accountStatus;
        await NoCTF.Infrastructure.Authentication.UserCredentialWrite.InvalidateTokensAsync(db, user, ct);
        user.UpdatedAt = now;
        RecordAccountStatusChange(user, actorUserId, accountStatus, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(UpdatePlatformUserStatusState.Updated, Map(user));
    }

    public async Task<UpdatePlatformUserEmailVerificationResult> UpdateEmailVerificationAsync(
        Guid userId,
        Guid actorUserId,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return new(UpdatePlatformUserEmailVerificationState.UserNotFound);
        if (user.AccountStatus == UserAccountStatus.Anonymized)
        {
            return new(
                UpdatePlatformUserEmailVerificationState.AnonymizedAccountImmutable);
        }

        var currentlyVerified = user.EmailVerifiedAt is not null;
        if (currentlyVerified == emailVerified)
        {
            return new(UpdatePlatformUserEmailVerificationState.Updated, Map(user));
        }

        user.EmailVerifiedAt = emailVerified ? now : null;
        await NoCTF.Infrastructure.Authentication.UserCredentialWrite.InvalidateTokensAsync(db, user, ct);
        user.UpdatedAt = now;
        RecordEmailVerificationChange(user, actorUserId, emailVerified, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(UpdatePlatformUserEmailVerificationState.Updated, Map(user));
    }

    public async Task<PatchPlatformUserResult> PatchUserAsync(
        Guid userId,
        Guid actorUserId,
        Action<User> apply,
        bool? emailVerified,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        await ActiveHumanAdministratorMutationGuard.AcquireAsync(db, ct);
        await ResourceManagerRoleGuard.AcquireAsync(db, [userId], ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return new(PatchPlatformUserState.UserNotFound);
        if (user.AccountStatus == UserAccountStatus.Anonymized)
            return new(PatchPlatformUserState.AnonymizedAccountImmutable);

        var originalRole = user.Role;
        var originalStatus = user.AccountStatus;
        var originallyVerified = user.EmailVerifiedAt is not null;
        apply(user);
        if (user.AccountStatus == UserAccountStatus.Anonymized)
        {
            db.ChangeTracker.Clear();
            return new(PatchPlatformUserState.AnonymizedAccountImmutable);
        }
        var removesActiveAdministrator = user.Kind == UserKind.Human
            && originalRole == UserRole.Administrator
            && originalStatus == UserAccountStatus.Active
            && (user.Role != UserRole.Administrator
                || user.AccountStatus != UserAccountStatus.Active);
        if (removesActiveAdministrator
            && await ActiveHumanAdministratorMutationGuard.CountAsync(db, ct) <= 1)
        {
            db.ChangeTracker.Clear();
            return new(PatchPlatformUserState.LastAdministratorProtected);
        }

        if (user.Role == UserRole.User && originalRole.CanManageResources())
        {
            var competitionIds = await db.Competitions.AsNoTracking()
                .Where(competition => competition.OwnerId == userId
                    || competition.Collaborators.Any(collaborator => collaborator.Role == NoCTF.Domain.Competitions.CompetitionCollaboratorRole.Manager && collaborator.UserId == userId))
                .OrderBy(competition => competition.Id)
                .Select(competition => competition.Id)
                .ToArrayAsync(ct);
            var challengeIds = await db.Challenges.AsNoTracking()
                .Where(challenge => challenge.OwnerId == userId
                    || challenge.Managers.Any(manager => manager.UserId == userId))
                .OrderBy(challenge => challenge.Id)
                .Select(challenge => challenge.Id)
                .ToArrayAsync(ct);
            if (competitionIds.Length > 0 || challengeIds.Length > 0)
            {
                db.ChangeTracker.Clear();
                return new(
                    PatchPlatformUserState.ActiveOwnerOrManagerAssignments,
                    Blockers: new(competitionIds, challengeIds));
            }
        }

        if (emailVerified is not null)
            user.EmailVerifiedAt = emailVerified.Value ? now : null;
        var roleChanged = user.Role != originalRole;
        var statusChanged = user.AccountStatus != originalStatus;
        var verificationChanged = (user.EmailVerifiedAt is not null) != originallyVerified;
        if (!roleChanged && !statusChanged && !verificationChanged)
        {
            await transaction.CommitAsync(ct);
            return new(PatchPlatformUserState.Updated, Map(user));
        }

        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        if (statusChanged)
            RecordAccountStatusChange(user, actorUserId, user.AccountStatus, now);
        if (verificationChanged)
            RecordEmailVerificationChange(
                user,
                actorUserId,
                user.EmailVerifiedAt is not null,
                now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(PatchPlatformUserState.Updated, Map(user));
    }

    public async Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await AggregateCompatibleTransaction.BeginAsync(db, ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return null;
        await NoCTF.Infrastructure.Authentication.UserCredentialWrite.InvalidateTokensAsync(db, user, ct);
        user.UpdatedAt = now;
        var auditFact = new PlatformUserTokenAuditFact(
            1,
            user.Id,
            user.UserName,
            PlatformUserTokenAdministrationAction.TokensInvalidated,
            null,
            null,
            null,
            user.TokenVersion);
        RecordTokenAudit(
            actorUserId,
            auditFact,
            Guid.CreateVersion7(now),
            EntityReferenceKind.User,
            user.Id,
            now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Map(user);
    }

    private static PlatformUserView Map(User user) =>
        new(
            user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
            user.TokenVersion,
            user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt,
            user.ExternalIdentityProviderId,
            user.ExternalIdentityProtocol,
            user.ExternalIdentitySubject,
            user.ExternalIdentityBoundAt);

    private void RecordAccountStatusChange(
        User user,
        Guid actorUserId,
        UserAccountStatus accountStatus,
        DateTimeOffset occurredAt)
    {
        var action = accountStatus switch
        {
            UserAccountStatus.Active => UserAccountLifecycleAction.Activated,
            UserAccountStatus.Banned => UserAccountLifecycleAction.Banned,
            UserAccountStatus.Disabled => UserAccountLifecycleAction.Disabled,
            _ => throw new InvalidOperationException(
                $"Account status {accountStatus} cannot be assigned by an administrator.")
        };
        db.Notifications.Add(new UserAccountLifecycleChangedNotification
        {
            Id = Guid.CreateVersion7(occurredAt),
            SourceType = NotificationSourceType.User,
            SourceId = actorUserId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            UserId = user.Id,
            UserName = user.UserName,
            UserLifecycleAction = action,
            Reason = accountStatus switch
            {
                UserAccountStatus.Active => "manual_activate",
                UserAccountStatus.Banned => "manual_ban",
                UserAccountStatus.Disabled => "manual_disable",
                _ => throw new InvalidOperationException(
                    $"Unsupported account status {accountStatus}.")
            },
            Automatic = false,
            RelatedType = EntityReferenceKind.User,
            RelatedId = user.Id,
            SentAt = occurredAt
        });
    }

    private void RecordEmailVerificationChange(
        User user,
        Guid actorUserId,
        bool emailVerified,
        DateTimeOffset occurredAt)
    {
        db.Notifications.Add(new UserAccountLifecycleChangedNotification
        {
            Id = Guid.CreateVersion7(occurredAt),
            SourceType = NotificationSourceType.User,
            SourceId = actorUserId,
            TargetType = NotificationTargetType.PlatformAdministrators,
            TargetId = Notification.PlatformAdministratorsTargetId,
            UserId = user.Id,
            UserName = user.UserName,
            UserLifecycleAction = emailVerified
                ? UserAccountLifecycleAction.EmailVerified
                : UserAccountLifecycleAction.EmailUnverified,
            Reason = emailVerified ? "manual_verify_email" : "manual_unverify_email",
            Automatic = false,
            RelatedType = EntityReferenceKind.User,
            RelatedId = user.Id,
            SentAt = occurredAt
        });
    }

    private void RecordTokenAudit(
        Guid actorUserId,
        PlatformUserTokenAuditFact fact,
        Guid notificationId,
        EntityReferenceKind relatedType,
        Guid relatedId,
        DateTimeOffset occurredAt)
    {
        var notification = NotificationGeneratedCatalog.Create(fact.Action switch
        {
            PlatformUserTokenAdministrationAction.AccessTokenIssued =>
                NotificationKind.PlatformUserAccessTokenIssued,
            PlatformUserTokenAdministrationAction.AccessTokenRevoked =>
                NotificationKind.PlatformUserAccessTokenRevoked,
            PlatformUserTokenAdministrationAction.TokensInvalidated =>
                NotificationKind.PlatformUserTokensInvalidated,
            _ => throw new InvalidOperationException(
                $"Unsupported platform token action {fact.Action}.")
        });
        notification.Id = notificationId;
        notification.SourceType = NotificationSourceType.User;
        notification.SourceId = actorUserId;
        notification.TargetType = NotificationTargetType.PlatformAdministrators;
        notification.TargetId = Notification.PlatformAdministratorsTargetId;
        notification.UserId = fact.TargetUserId;
        notification.UserName = fact.TargetUserName;
        notification.ActionValue = (int)fact.Action;
        notification.JwtId = fact.JwtId;
        notification.PayloadExpiresAt = fact.ExpiresAt;
        notification.Reason = fact.Reason;
        notification.Count = fact.TokenVersion;
        notification.RelatedType = relatedType;
        notification.RelatedId = relatedId;
        notification.SentAt = occurredAt;
        db.Notifications.Add(notification);
    }

}

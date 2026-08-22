using System.Text.Json;
using NoCTF.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using NoCTF.Application.Administration;
using NoCTF.Application.Administration.UserAccounts;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Notifications;
using NoCTF.Domain.Shared;
using NoCTF.Infrastructure.Administration;
using Wolverine.Persistence.Durability;

namespace NoCTF.Infrastructure.Administration;

public sealed class PlatformAdministrationStore(
    NoCtfDbContext db,
    IProcessDeadLetterStore deadLetters,
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
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
            .ToListAsync(ct);

    public Task<PlatformUserView?> FindUserAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new PlatformUserView(
                user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
                user.TokenVersion,
                user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt))
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
        var email = BotIdentity.DummyEmail(id);
        var user = new User
        {
            Id = id,
            UserName = trimmedUserName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await ActiveHumanAdministratorMutationGuard.AcquireAsync(db, ct);
        await ResourceManagerRoleGuard.AcquireAsync(db, [userId], ct);
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return new(UpdatePlatformRoleState.UserNotFound);
        if (user.Kind == UserKind.Bot && role == UserRole.Administrator)
            return new(UpdatePlatformRoleState.InvalidBotRole);
        if (ActiveHumanAdministratorMutationGuard.Contains(user)
            && role != UserRole.Administrator
            && await ActiveHumanAdministratorMutationGuard.CountAsync(db, ct) <= 1)
        {
            return new(UpdatePlatformRoleState.LastAdministratorProtected);
        }
        if (role == UserRole.User && user.Role.CanManageResources())
        {
            var competitionIds = await db.Competitions.AsNoTracking()
                .Where(competition =>
                    competition.OwnerId == userId ||
                    competition.ManagerIds.Contains(userId))
                .OrderBy(competition => competition.Id)
                .Select(competition => competition.Id)
                .ToArrayAsync(ct);
            var challengeIds = await db.Challenges.AsNoTracking()
                .Where(challenge =>
                    challenge.OwnerId == userId ||
                    challenge.ManagerIds.Contains(userId))
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
        user.TokenVersion = checked(user.TokenVersion + 1);
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
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
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
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        RecordAccountStatusChange(user, actorUserId, accountStatus, now);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(UpdatePlatformUserStatusState.Updated, Map(user));
    }

    public async Task<PlatformUserView?> InvalidateTokensAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return null;
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return Map(user);
    }

    public async Task<IReadOnlyList<DeadLetterView>> ListDeadLettersAsync(
        int limit,
        CancellationToken ct)
    {
        return (await deadLetters.ListAsync(limit, ct)).Select(Map).ToArray();
    }

    public async Task<DeadLetterView?> FindDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        var result = await deadLetters.FindAsync(messageId, ct);
        return result is null ? null : Map(result);
    }

    public async Task<bool> RequeueDeadLetterAsync(Guid messageId, CancellationToken ct)
    {
        return await deadLetters.ReplayAsync(messageId, ct);
    }

    private static PlatformUserView Map(User user) =>
        new(
            user.Id, user.UserName, user.Email, user.Kind, user.Role, user.AccountStatus,
            user.TokenVersion,
            user.EmailVerifiedAt != null, user.CreatedAt, user.UpdatedAt);

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
                user.UserName,
                action,
                accountStatus switch
                {
                    UserAccountStatus.Active => "manual_activate",
                    UserAccountStatus.Banned => "manual_ban",
                    UserAccountStatus.Disabled => "manual_disable",
                    _ => throw new InvalidOperationException(
                        $"Unsupported account status {accountStatus}.")
                },
                false), JsonOptions),
            RelatedType = EntityReferenceKind.User,
            RelatedId = user.Id,
            SentAt = occurredAt
        });
    }

    private static DeadLetterView Map(DeadLetterEnvelope envelope) =>
        new(
            envelope.Id,
            envelope.MessageType,
            envelope.Source,
            envelope.ExceptionType,
            envelope.SentAt,
            envelope.Replayable);
}

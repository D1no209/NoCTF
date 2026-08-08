using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AuthenticationStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITransactionalMessageOutbox? messageOutbox = null,
    FileReferenceLock? fileReferenceLock = null) : IUserAuthenticationStore
{
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new OpenApiTransactionalMessageOutbox();
    private readonly FileReferenceLock fileLock = fileReferenceLock ?? new FileReferenceLock();
    public async Task<AuthenticatedUser?> FindByLoginAsync(
        string login,
        CancellationToken ct)
    {
        var normalized = login.Trim().ToUpperInvariant();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active
                && (item.NormalizedEmail == normalized
                || item.NormalizedUserName == normalized
                || item.Id.ToString() == login),
            ct);
        return ToAuthenticated(user);
    }

    public async Task<bool> VerifyPasswordAsync(
        Guid userId,
        string password,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null || user.Kind != UserKind.Human)
            return false;
        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            user.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return result != PasswordVerificationResult.Failed;
    }

    public async Task<AuthenticatedUser?> FindByIdAsync(Guid userId, CancellationToken ct) =>
        ToAuthenticated(await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct));

    public Task<UserProfile?> GetProfileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active)
            .Select(user => new UserProfile(
                user.Id,
                user.UserName,
                user.Email,
                user.Role,
                user.Kind,
                user.EmailVerifiedAt != null,
                user.Description,
                user.AvatarFileId,
                user.IsEmailPublic))
            .SingleOrDefaultAsync(ct);

    public async Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        bool isEmailPublic,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;

        user.Description = description;
        user.IsEmailPublic = isEmailPublic;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task<UserAvatarReplacement?> ReplaceAvatarAsync(
        Guid userId,
        Guid fileId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;
        if (!await fileLock.AcquireAsync(db, fileId, ct))
            return null;

        var previousFileId = user.AvatarFileId;
        user.AvatarFileId = fileId;
        user.UpdatedAt = now;
        if (previousFileId is { } previous && previous != fileId)
            await outbox.PublishAsync(new CleanupFile(previous));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return new UserAvatarReplacement(ToProfile(user), previousFileId);
    }

    public Task<BusinessFileReference?> GetAvatarFileAsync(Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(user => user.Id == userId
                && user.AccountStatus == UserAccountStatus.Active
                && user.AvatarFileId != null)
            .Join(db.Files.AsNoTracking(), user => user.AvatarFileId, file => file.Id,
                (_, file) => new BusinessFileReference(
                    file.Id, file.ObjectKey, file.FileName, file.ContentType))
            .SingleOrDefaultAsync(ct);

    public async Task<CreateUserState> CreateAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var trimmedUserName = userName.Trim();
        var trimmedEmail = email.Trim();
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        var normalizedEmail = trimmedEmail.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return CreateUserState.UserNameConflict;
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, ct))
            return CreateUserState.EmailConflict;

        var user = new User
        {
            Id = userId,
            UserName = trimmedUserName,
            NormalizedUserName = normalizedUserName,
            Email = trimmedEmail,
            NormalizedEmail = normalizedEmail,
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = emailVerified ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return CreateUserState.Created;
        }
        catch (DbUpdateException)
        {
            db.Entry(user).State = EntityState.Detached;
            return await db.Users.AnyAsync(
                existing => existing.NormalizedEmail == normalizedEmail, ct)
                ? CreateUserState.EmailConflict
                : CreateUserState.UserNameConflict;
        }
    }

    public async Task<ChangePasswordState> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null
            || user.Kind != UserKind.Human
            || passwordHasher.VerifyHashedPassword(
                user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            return ChangePasswordState.CurrentPasswordInvalid;

        user.PasswordHash = passwordHasher.HashPassword(user, newPassword);
        user.TokenVersion = checked(user.TokenVersion + 1);
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ChangePasswordState.Changed;
    }

    public async Task<bool> IncrementTokenVersionAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await db.Users.Where(user =>
                user.Id == userId && user.AccountStatus == UserAccountStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.TokenVersion, user => user.TokenVersion + 1)
                    .SetProperty(user => user.UpdatedAt, now),
                ct) == 1;

    private static AuthenticatedUser? ToAuthenticated(User? user) =>
        user is null
            ? null
            : new(
                user.Id,
                user.UserName,
                user.Role,
                user.Kind,
                user.TokenVersion,
                user.EmailVerifiedAt is not null);

    private static UserProfile ToProfile(User user) =>
        new(
            user.Id,
            user.UserName,
            user.Email,
            user.Role,
            user.Kind,
            user.EmailVerifiedAt is not null,
            user.Description,
            user.AvatarFileId,
            user.IsEmailPublic);
}

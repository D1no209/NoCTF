using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;
using NoCTF.Application.Storage;
using NoCTF.Domain.Storage;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Infrastructure.Messaging;
using NoCTF.Infrastructure.Storage;

namespace NoCTF.Infrastructure.Authentication;

public sealed class AuthenticationStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITransactionalMessageOutbox? messageOutbox = null,
    FileReferenceLock? fileReferenceLock = null,
    TimeProvider? clock = null,
    IEmailVerificationConfigurationStore? emailVerificationConfiguration = null,
    ILogger<AuthenticationStore>? logger = null)
    : IUserAuthenticationStore,
        IUserRegistrationStore
{
    private readonly TimeProvider timeProvider = clock ?? TimeProvider.System;
    private readonly ITransactionalMessageOutbox outbox =
        messageOutbox ?? new NoOpTransactionalMessageOutbox();
    private readonly FileReferenceLock fileLock = fileReferenceLock ?? new FileReferenceLock();
    public async Task<AuthenticatedUser?> FindByLoginAsync(
        string login,
        CancellationToken ct)
    {
        var normalizedUserName = login.Trim().ToUpperInvariant();
        var canonicalEmail = EmailCanonicalizer.Canonicalize(login);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(
            item => item.Kind == UserKind.Human
                && item.AccountStatus == UserAccountStatus.Active
                && (item.Email == canonicalEmail
                || item.NormalizedUserName == normalizedUserName
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
            user.UpdatedAt = timeProvider.GetUtcNow();
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
                user.AvatarFileId))
            .SingleOrDefaultAsync(ct);

    public async Task<UserProfile?> UpdateProfileAsync(
        Guid userId,
        string? description,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item =>
            item.Id == userId && item.AccountStatus == UserAccountStatus.Active,
            ct);
        if (user is null)
            return null;

        user.Description = description;
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
        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return CreateUserState.UserNameConflict;
        if (await db.Users.AnyAsync(user => user.Email == canonicalEmail, ct))
            return CreateUserState.EmailConflict;

        var user = CreateUser(
            userId,
            trimmedUserName,
            normalizedUserName,
            canonicalEmail,
            password,
            emailVerified,
            now);
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
                existing => existing.Email == canonicalEmail, ct)
                ? CreateUserState.EmailConflict
                : CreateUserState.UserNameConflict;
        }
    }

    public async Task<CreateRegisteredUserResult> RegisterAsync(
        Guid userId,
        string userName,
        string email,
        string password,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var trimmedUserName = userName.Trim();
        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var normalizedUserName = trimmedUserName.ToUpperInvariant();
        var settings = emailVerificationConfiguration is null
            ? null
            : await emailVerificationConfiguration.GetAsync(ct);
        var verificationState = settings?.Enabled == true
            ? DeliveryConfigured(settings)
                ? EmailVerificationState.Issued
                : EmailVerificationState.DeliveryNotConfigured
            : EmailVerificationState.Disabled;

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        if (await db.Users.AnyAsync(user => user.NormalizedUserName == normalizedUserName, ct))
            return new(CreateUserState.UserNameConflict, verificationState);
        if (await db.Users.AnyAsync(user => user.Email == canonicalEmail, ct))
            return new(CreateUserState.EmailConflict, verificationState);

        var user = CreateUser(
            userId,
            trimmedUserName,
            normalizedUserName,
            canonicalEmail,
            password,
            emailVerified: verificationState == EmailVerificationState.Disabled,
            now);
        db.Users.Add(user);

        string? verificationToken = null;
        if (verificationState == EmailVerificationState.Issued)
        {
            verificationToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
                RandomNumberGenerator.GetBytes(32));
            db.AccountTokens.Add(new AccountToken
            {
                Id = Guid.CreateVersion7(now),
                UserId = userId,
                Kind = AccountTokenKind.EmailVerification,
                TokenSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(verificationToken)),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(settings!.TokenLifetimeMinutes)
            });
        }

        try
        {
            // Persist the user and token before publishing so a uniqueness failure cannot
            // leave an in-memory envelope for an account that was never created. The
            // transaction remains open and the scoped Wolverine outbox permits the second
            // flush that atomically stores the envelope below.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return new(
                await db.Users.AnyAsync(existing => existing.Email == canonicalEmail, ct)
                    ? CreateUserState.EmailConflict
                    : CreateUserState.UserNameConflict,
                verificationState);
        }

        if (verificationToken is not null)
        {
            await outbox.PublishAsync(new SendEmailVerification(userId, verificationToken));
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
        if (verificationToken is not null)
            await outbox.FlushOutgoingMessagesAsync();
        ObserveRegistrationVerification(verificationState, userId);
        return new(CreateUserState.Created, verificationState);
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
        await outbox.PublishAsync(new SendPasswordChangedNotification(user.Id));
        await db.SaveChangesAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
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
            user.AvatarFileId);

    private User CreateUser(
        Guid userId,
        string userName,
        string normalizedUserName,
        string email,
        string password,
        bool emailVerified,
        DateTimeOffset now)
    {
        var user = new User
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = email,
            Kind = UserKind.Human,
            Role = UserRole.User,
            AccountStatus = UserAccountStatus.Active,
            EmailVerifiedAt = emailVerified ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, password);
        return user;
    }

    private static bool DeliveryConfigured(EmailVerificationConfigurationView settings) =>
        !string.IsNullOrWhiteSpace(settings.SmtpHost)
        && !string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
        && (string.IsNullOrWhiteSpace(settings.SmtpUserName)
            || settings.SmtpPasswordConfigured);

    private void ObserveRegistrationVerification(
        EmailVerificationState state,
        Guid userId)
    {
        NoCtfTelemetry.RecordAccountNotificationIssuance(
            "email_verification",
            state.ToString());
        logger?.LogInformation(
            "Registration email verification issuance completed with {Outcome} for user {UserId}.",
            state,
            userId);
    }
}

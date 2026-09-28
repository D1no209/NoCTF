using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class PasswordResetStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    IEmailVerificationConfigurationStore configuration,
    IEmailVerificationDeliveryConfigurationReader deliveryConfiguration,
    IPostCommitMessagePublisher outbox,
    ILogger<PasswordResetStore>? logger = null) : IPasswordResetStore
{
    public async Task<PasswordResetRequestState> IssueAsync(
        string email,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await configuration.GetAsync(ct);
        if (await deliveryConfiguration.GetDeliveryConfigurationAsync(
                requireEnabled: false,
                ct) is null)
        {
            return Observe(PasswordResetRequestState.DeliveryNotConfigured, null);
        }

        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var userId = await db.Users.AsNoTracking()
            .Where(user => user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active
                && user.EmailVerifiedAt != null
                && user.Email == canonicalEmail)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(ct);
        if (userId is null)
            return Observe(PasswordResetRequestState.Ignored, null);

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        await AcquireUserLockAsync(userId.Value, ct);

        var userExists = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId.Value
            && user.Kind == UserKind.Human
            && user.AccountStatus == UserAccountStatus.Active
            && user.EmailVerifiedAt != null
            && user.Email == canonicalEmail,
            ct);
        if (!userExists)
            return Observe(PasswordResetRequestState.Ignored, userId);

        var hourlyBoundary = now.AddHours(-1);
        var recentRequests = await db.AccountTokens.CountAsync(token =>
            token.UserId == userId.Value
            && token.Kind == AccountTokenKind.PasswordReset
            && token.CreatedAt > hourlyBoundary,
            ct);
        if (recentRequests >= settings.PasswordResetMaxRequestsPerHour)
            return Observe(PasswordResetRequestState.RateLimited, userId);

        var cooldownBoundary = now.AddSeconds(-settings.PasswordResetCooldownSeconds);
        if (await db.AccountTokens.AsNoTracking().AnyAsync(token =>
                token.UserId == userId.Value
                && token.Kind == AccountTokenKind.PasswordReset
                && token.CreatedAt > cooldownBoundary,
                ct))
        {
            return Observe(PasswordResetRequestState.RateLimited, userId);
        }

        await db.AccountTokens
            .Where(token => token.UserId == userId.Value
                && token.Kind == AccountTokenKind.PasswordReset
                && token.ConsumedAt == null
                && token.InvalidatedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.InvalidatedAt, now), ct);

        var rawToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        db.AccountTokens.Add(new AccountToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId.Value,
            Kind = AccountTokenKind.PasswordReset,
            TokenSha256 = HashToken(rawToken),
            ExpiresAt = now.AddMinutes(settings.PasswordResetTokenLifetimeMinutes),
            CreatedAt = now
        });
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || TransactionFailureClassifier.IsRetryable(exception))
        {
            await TryRollbackAsync(transaction, ct);
            db.ChangeTracker.Clear();
            return Observe(PasswordResetRequestState.RateLimited, userId);
        }
        await outbox.PublishAsync(new SendPasswordReset(userId.Value, rawToken));
        await outbox.FlushCommittedMessagesAsync();
        return Observe(PasswordResetRequestState.Queued, userId);
    }

    public async Task<PasswordResetCompletionState> CompleteAsync(
        string token,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tokenHash = HashToken(token);
        var descriptor = await db.AccountTokens.AsNoTracking()
            .Where(candidate => candidate.Kind == AccountTokenKind.PasswordReset
                && candidate.TokenSha256 == tokenHash)
            .Select(candidate => new { candidate.Id, candidate.UserId })
            .SingleOrDefaultAsync(ct);
        if (descriptor is null)
            return PasswordResetCompletionState.InvalidOrExpired;

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            ct);
        await AcquireUserLockAsync(descriptor.UserId, ct);
        var resetToken = await db.AccountTokens
            .SingleOrDefaultAsync(token => token.Id == descriptor.Id, ct);
        if (resetToken is null
            || !CryptographicOperations.FixedTimeEquals(resetToken.TokenSha256, tokenHash)
            || resetToken.ConsumedAt is not null
            || resetToken.InvalidatedAt is not null
            || resetToken.ExpiresAt <= now)
        {
            return PasswordResetCompletionState.InvalidOrExpired;
        }

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate =>
            candidate.Id == resetToken.UserId
            && candidate.Kind == UserKind.Human
            && candidate.AccountStatus == UserAccountStatus.Active
            && candidate.EmailVerifiedAt != null,
            ct);
        if (user is null)
            return PasswordResetCompletionState.InvalidOrExpired;

        var passwordHash = passwordHasher.HashPassword(user, newPassword);
        try
        {
            if (!await UserCredentialWrite.ReplaceAsync(
                    db,
                    user,
                    passwordHash,
                    invalidateTokens: true,
                    now,
                    ct))
                return PasswordResetCompletionState.InvalidOrExpired;
        }
        catch (Exception exception) when (TransactionFailureClassifier.IsRetryable(exception))
        {
            return PasswordResetCompletionState.InvalidOrExpired;
        }

        resetToken.ConsumedAt = now;
        await UserCredentialWrite.InvalidateResetTokensAsync(db, user.Id, resetToken.Id, now, ct);
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException
            || TransactionFailureClassifier.IsRetryable(exception))
        {
            await TryRollbackAsync(transaction, ct);
            db.ChangeTracker.Clear();
            return PasswordResetCompletionState.InvalidOrExpired;
        }
        await outbox.PublishAsync(new SendPasswordChangedNotification(user.Id));
        await outbox.FlushCommittedMessagesAsync();
        return PasswordResetCompletionState.Reset;
    }

    private async Task AcquireUserLockAsync(Guid userId, CancellationToken ct)
    {
        _ = await db.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, ct);
    }

    private static async Task TryRollbackAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken ct)
    {
        try
        {
            await transaction.RollbackAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // A provider may mark a serialization-failed transaction completed before surfacing
            // the standardized 40001 error. There is nothing left to roll back in that case.
        }
    }

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private PasswordResetRequestState Observe(
        PasswordResetRequestState state,
        Guid? userId)
    {
        NoCtfTelemetry.RecordAccountNotificationIssuance(
            "password_reset",
            state.ToString());
        logger?.LogInformation(
            "Password reset token issuance completed with {Outcome} for user {UserId}.",
            state,
            userId);
        return state;
    }
}

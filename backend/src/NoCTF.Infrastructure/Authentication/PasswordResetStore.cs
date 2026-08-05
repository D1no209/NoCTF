using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Authentication.PasswordReset;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class PasswordResetStore(
    NoCtfDbContext db,
    IPasswordHasher<User> passwordHasher,
    IEmailVerificationConfigurationStore configuration,
    IEmailVerificationDeliveryConfigurationReader deliveryConfiguration,
    ITransactionalMessageOutbox outbox) : IPasswordResetStore
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
            return PasswordResetRequestState.DeliveryNotConfigured;
        }

        var normalizedEmail = email.Trim().ToUpperInvariant();
        var userId = await db.Users.AsNoTracking()
            .Where(user => user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active
                && user.EmailVerifiedAt != null
                && user.NormalizedEmail == normalizedEmail)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(ct);
        if (userId is null)
            return PasswordResetRequestState.Ignored;

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await AcquireUserLockAsync(userId.Value, ct);

        var userExists = await db.Users.AsNoTracking().AnyAsync(user =>
            user.Id == userId.Value
            && user.Kind == UserKind.Human
            && user.AccountStatus == UserAccountStatus.Active
            && user.EmailVerifiedAt != null
            && user.NormalizedEmail == normalizedEmail,
            ct);
        if (!userExists)
            return PasswordResetRequestState.Ignored;

        var hourlyBoundary = now.AddHours(-1);
        var recentRequests = await db.PasswordResetTokens.CountAsync(token =>
            token.UserId == userId.Value && token.CreatedAt > hourlyBoundary,
            ct);
        if (recentRequests >= settings.PasswordResetMaxRequestsPerHour)
            return PasswordResetRequestState.RateLimited;

        var cooldownBoundary = now.AddSeconds(-settings.PasswordResetCooldownSeconds);
        if (await db.PasswordResetTokens.AsNoTracking().AnyAsync(token =>
                token.UserId == userId.Value && token.CreatedAt > cooldownBoundary,
                ct))
        {
            return PasswordResetRequestState.RateLimited;
        }

        await db.PasswordResetTokens
            .Where(token => token.UserId == userId.Value
                && token.ConsumedAt == null
                && token.InvalidatedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(token => token.InvalidatedAt, now), ct);

        var rawToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(
            RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(new PasswordResetToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId.Value,
            TokenSha256 = HashToken(rawToken),
            ExpiresAt = now.AddMinutes(settings.PasswordResetTokenLifetimeMinutes),
            CreatedAt = now
        });
        await outbox.PublishAsync(new SendPasswordReset(userId.Value, rawToken));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return PasswordResetRequestState.Queued;
    }

    public async Task<PasswordResetCompletionState> CompleteAsync(
        string token,
        string newPassword,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tokenHash = HashToken(token);
        var descriptor = await db.PasswordResetTokens.AsNoTracking()
            .Where(candidate => candidate.TokenSha256 == tokenHash)
            .Select(candidate => new { candidate.Id, candidate.UserId })
            .SingleOrDefaultAsync(ct);
        if (descriptor is null)
            return PasswordResetCompletionState.InvalidOrExpired;

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await AcquireUserLockAsync(descriptor.UserId, ct);
        var resetToken = await db.PasswordResetTokens
            .FromSqlInterpolated(
                $"SELECT * FROM password_reset_tokens WHERE id = {descriptor.Id} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
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
        var updated = await db.Users
            .Where(candidate => candidate.Id == user.Id
                && candidate.Kind == UserKind.Human
                && candidate.AccountStatus == UserAccountStatus.Active
                && candidate.EmailVerifiedAt != null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.PasswordHash, passwordHash)
                .SetProperty(
                    candidate => candidate.TokenVersion,
                    candidate => candidate.TokenVersion + 1)
                .SetProperty(candidate => candidate.UpdatedAt, now), ct);
        if (updated != 1)
            return PasswordResetCompletionState.InvalidOrExpired;

        resetToken.ConsumedAt = now;
        await db.PasswordResetTokens
            .Where(candidate => candidate.UserId == user.Id
                && candidate.Id != resetToken.Id
                && candidate.ConsumedAt == null
                && candidate.InvalidatedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.InvalidatedAt, now), ct);
        await outbox.PublishAsync(new SendPasswordChangedNotification(user.Id));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return PasswordResetCompletionState.Reset;
    }

    private Task AcquireUserLockAsync(Guid userId, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({'p' + userId.ToString("N")}, 0))",
            ct);

    private static byte[] HashToken(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

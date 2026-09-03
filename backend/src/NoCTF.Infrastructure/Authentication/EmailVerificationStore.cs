using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Messaging;
using NoCTF.Application.Observability;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EmailVerificationStore(
    NoCtfDbContext db,
    IEmailVerificationConfigurationStore configuration,
    ITransactionalMessageOutbox outbox,
    ILogger<EmailVerificationStore>? logger = null) : IEmailVerificationStore
{
    public async Task<bool> IsRequiredAsync(CancellationToken ct) =>
        (await configuration.GetAsync(ct)).Enabled;

    public async Task<EmailVerificationState> IssueByEmailAsync(
        string email,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var canonicalEmail = EmailCanonicalizer.Canonicalize(email);
        var userId = await db.Users.AsNoTracking()
            .Where(user => user.Kind == UserKind.Human
                && user.AccountStatus == UserAccountStatus.Active
                && user.EmailVerifiedAt == null
                && user.Email == canonicalEmail)
            .Select(user => (Guid?)user.Id)
            .SingleOrDefaultAsync(ct);
        return userId is null
            ? Observe(EmailVerificationState.UserNotFound, null)
            : await IssueAsync(userId.Value, now, ct);
    }

    public async Task<EmailVerificationState> IssueAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            ct);
        await AcquireUserLockAsync(userId, ct);

        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return Observe(EmailVerificationState.UserNotFound, userId);
        if (user.EmailVerifiedAt is not null)
            return Observe(EmailVerificationState.AlreadyVerified, userId);

        var settings = await configuration.GetAsync(ct);
        if (!settings.Enabled)
        {
            user.EmailVerifiedAt = now;
            user.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Observe(EmailVerificationState.Disabled, userId);
        }
        if (string.IsNullOrWhiteSpace(settings.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
            || (!string.IsNullOrWhiteSpace(settings.SmtpUserName)
                && !settings.SmtpPasswordConfigured))
        {
            return Observe(EmailVerificationState.DeliveryNotConfigured, userId);
        }

        var resendBoundary = now.AddSeconds(-settings.ResendCooldownSeconds);
        var sentRecently = await db.AccountTokens.AsNoTracking().AnyAsync(
            item => item.UserId == userId
                && item.Kind == AccountTokenKind.EmailVerification
                && item.ConsumedAt == null
                && item.CreatedAt > resendBoundary,
            ct);
        if (sentRecently)
            return Observe(EmailVerificationState.RateLimited, userId);

        await db.AccountTokens
            .Where(item => item.UserId == userId
                && item.Kind == AccountTokenKind.EmailVerification
                && item.ConsumedAt == null
                && item.InvalidatedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(item => item.InvalidatedAt, now),
                ct);

        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes);
        db.AccountTokens.Add(new AccountToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Kind = AccountTokenKind.EmailVerification,
            TokenSha256 = SHA256.HashData(Encoding.UTF8.GetBytes(token)),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(settings.TokenLifetimeMinutes)
        });
        await outbox.PublishAsync(new SendEmailVerification(userId, token));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return Observe(EmailVerificationState.Issued, userId);
    }

    public async Task<EmailVerificationState> VerifyAsync(
        string token,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        var verification = await db.AccountTokens.SingleOrDefaultAsync(
            item => item.TokenSha256 == hash
                && item.Kind == AccountTokenKind.EmailVerification
                && item.ConsumedAt == null
                && item.InvalidatedAt == null
                && item.ExpiresAt > now,
            ct);
        if (verification is null)
            return EmailVerificationState.InvalidOrExpired;

        var user = await db.Users.SingleOrDefaultAsync(
            item => item.Id == verification.UserId, ct);
        if (user is null)
            return EmailVerificationState.InvalidOrExpired;
        verification.ConsumedAt = now;
        user.EmailVerifiedAt ??= now;
        user.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return EmailVerificationState.Verified;
    }

    private async Task AcquireUserLockAsync(Guid userId, CancellationToken ct)
    {
        if (!db.Database.IsRelational())
        {
            _ = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
            return;
        }

        _ = await db.Users
            .FromSqlInterpolated($"SELECT * FROM users WHERE id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(ct);
    }

    private EmailVerificationState Observe(
        EmailVerificationState state,
        Guid? userId)
    {
        NoCtfTelemetry.RecordAccountNotificationIssuance(
            "email_verification",
            state.ToString());
        logger?.LogInformation(
            "Email verification token issuance completed with {Outcome} for user {UserId}.",
            state,
            userId);
        return state;
    }
}

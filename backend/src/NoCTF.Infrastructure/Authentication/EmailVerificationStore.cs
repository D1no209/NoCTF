using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.Account;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Application.Messaging;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EmailVerificationStore(
    NoCtfDbContext db,
    IEmailVerificationConfigurationStore configuration,
    ITransactionalMessageOutbox outbox) : IEmailVerificationStore
{
    public async Task<bool> IsRequiredAsync(CancellationToken ct) =>
        (await configuration.GetAsync(ct)).Enabled;

    public async Task<EmailVerificationState> IssueAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, ct);
        if (user is null)
            return EmailVerificationState.UserNotFound;
        if (user.EmailVerifiedAt is not null)
            return EmailVerificationState.AlreadyVerified;

        var settings = await configuration.GetAsync(ct);
        if (!settings.Enabled)
        {
            user.EmailVerifiedAt = now;
            user.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            return EmailVerificationState.Disabled;
        }
        if (!settings.SmtpPasswordConfigured
            || string.IsNullOrWhiteSpace(settings.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.SmtpUserName)
            || string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return EmailVerificationState.DeliveryNotConfigured;
        }

        var resendBoundary = now.AddSeconds(-settings.ResendCooldownSeconds);
        var sentRecently = await db.EmailVerificationTokens.AsNoTracking().AnyAsync(
            item => item.UserId == userId
                && item.ConsumedAt == null
                && item.CreatedAt > resendBoundary,
            ct);
        if (sentRecently)
            return EmailVerificationState.RateLimited;

        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(bytes);
        db.EmailVerificationTokens.Add(new EmailVerificationToken
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            TokenSha256 = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(settings.TokenLifetimeMinutes)
        });
        await outbox.PublishAsync(new SendEmailVerification(userId, token));
        await db.SaveChangesAsync(ct);
        await outbox.FlushOutgoingMessagesAsync();
        return EmailVerificationState.Issued;
    }

    public async Task<EmailVerificationState> VerifyAsync(
        string token,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        var verification = await db.EmailVerificationTokens.SingleOrDefaultAsync(
            item => item.TokenSha256 == hash
                && item.ConsumedAt == null
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
}

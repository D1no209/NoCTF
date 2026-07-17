using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Core;
using NoCTF.Infrastructure;

namespace NoCTF.API.Auth;

public sealed class EmailVerificationOptions
{
    public const string SectionName = "EmailVerification";

    public bool Enabled { get; set; }
    public string PublicBaseUrl { get; set; } = "http://localhost:5173";
    public int TokenLifetimeMinutes { get; set; } = 1440;
    public int ResendCooldownSeconds { get; set; } = 60;
    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "NoCTF";
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed record EmailVerificationDispatchResult(bool Required, bool Sent);

public enum EmailVerificationAttempt
{
    Verified,
    AlreadyVerified,
    Invalid,
    Expired,
    Disabled
}

public interface IEmailVerificationService
{
    Task<bool> IsEnabledAsync(CancellationToken ct);
    Task<EmailVerificationDispatchResult> SendForRegistrationAsync(User user, CancellationToken ct);
    Task ResendAsync(string email, CancellationToken ct);
    Task<EmailVerificationAttempt> VerifyAsync(string token, CancellationToken ct);
}

public interface IVerificationEmailSender
{
    Task SendAsync(string recipient, string verificationUrl, CancellationToken ct);
    Task SendTestAsync(string recipient, CancellationToken ct);
}

internal sealed class EmailVerificationService(
    ApplicationDbContext db,
    IVerificationEmailSender emailSender,
    IEmailVerificationSettingsStore settingsStore,
    ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    public async Task<bool> IsEnabledAsync(CancellationToken ct)
        => await settingsStore.IsEnabledAsync(ct);

    public async Task<EmailVerificationDispatchResult> SendForRegistrationAsync(User user, CancellationToken ct)
    {
        var options = await settingsStore.GetAsync(ct);
        return await CreateAndSendAsync(user, options, enforceCooldown: false, ct);
    }

    public async Task ResendAsync(string email, CancellationToken ct)
    {
        var options = await settingsStore.GetAsync(ct);
        if (!options.Enabled)
            return;

        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (normalizedEmail.Length is < 3 or > UserInputLimits.EmailMaxLength)
            return;

        var user = db.Database.IsRelational()
            ? await db.Users.FirstOrDefaultAsync(
                candidate => EF.Property<string>(candidate, "NormalizedEmail") == normalizedEmail,
                ct)
            : await db.Users.FirstOrDefaultAsync(
                candidate => candidate.Email.ToLower() == normalizedEmail,
                ct);
        if (user is null || user.EmailVerifiedAt.HasValue)
            return;

        await CreateAndSendAsync(user, options, enforceCooldown: true, ct);
    }

    public async Task<EmailVerificationAttempt> VerifyAsync(string token, CancellationToken ct)
    {
        if (!await settingsStore.IsEnabledAsync(ct))
            return EmailVerificationAttempt.Disabled;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 128)
            return EmailVerificationAttempt.Invalid;

        var tokenHash = HashToken(token);
        var verificationToken = await db.EmailVerificationTokens
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == tokenHash, ct);
        if (verificationToken is null)
            return EmailVerificationAttempt.Invalid;

        var user = await db.Users.FirstOrDefaultAsync(candidate => candidate.Id == verificationToken.UserId, ct);
        if (user is null)
            return EmailVerificationAttempt.Invalid;
        if (user.EmailVerifiedAt.HasValue)
            return EmailVerificationAttempt.AlreadyVerified;
        if (verificationToken.ConsumedAt.HasValue)
            return EmailVerificationAttempt.Invalid;

        var now = DateTime.UtcNow;
        if (verificationToken.ExpiresAt <= now)
        {
            verificationToken.ConsumedAt = now;
            await db.SaveChangesAsync(ct);
            return EmailVerificationAttempt.Expired;
        }

        user.EmailVerifiedAt = now;
        user.UpdatedAt = now;
        var activeTokens = await db.EmailVerificationTokens
            .Where(candidate => candidate.UserId == user.Id && candidate.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var activeToken in activeTokens)
            activeToken.ConsumedAt = now;
        await db.SaveChangesAsync(ct);
        return EmailVerificationAttempt.Verified;
    }

    private async Task<EmailVerificationDispatchResult> CreateAndSendAsync(
        User user,
        EmailVerificationOptions options,
        bool enforceCooldown,
        CancellationToken ct)
    {
        if (!options.Enabled)
            return new EmailVerificationDispatchResult(Required: false, Sent: false);

        var now = DateTime.UtcNow;
        if (enforceCooldown)
        {
            var cooldownStart = now.AddSeconds(-options.ResendCooldownSeconds);
            var recentlyIssued = await db.EmailVerificationTokens.AnyAsync(
                token => token.UserId == user.Id &&
                         token.ConsumedAt == null &&
                         token.CreatedAt > cooldownStart,
                ct);
            if (recentlyIssued)
                return new EmailVerificationDispatchResult(Required: true, Sent: false);
        }

        var previousTokens = await db.EmailVerificationTokens
            .Where(token => token.UserId == user.Id && token.ConsumedAt == null)
            .ToListAsync(ct);
        foreach (var previousToken in previousTokens)
            previousToken.ConsumedAt = now;

        var rawToken = GenerateToken();
        var token = new EmailVerificationToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(options.TokenLifetimeMinutes)
        };
        db.EmailVerificationTokens.Add(token);
        await db.SaveChangesAsync(ct);

        var verificationUrl = $"{options.PublicBaseUrl.TrimEnd('/')}/verify-email?token={Uri.EscapeDataString(rawToken)}";
        try
        {
            await emailSender.SendAsync(user.Email, verificationUrl, ct);
            return new EmailVerificationDispatchResult(Required: true, Sent: true);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            token.ConsumedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            logger.LogWarning(
                "Email verification delivery failed for user {UserId}: {ErrorType}",
                user.Id,
                ex.GetType().Name);
            return new EmailVerificationDispatchResult(Required: true, Sent: false);
        }
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

internal sealed class SmtpVerificationEmailSender(IEmailVerificationSettingsStore settingsStore)
    : IVerificationEmailSender
{
    public async Task SendAsync(string recipient, string verificationUrl, CancellationToken ct)
    {
        var smtp = (await settingsStore.GetAsync(ct)).Smtp;
        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = "Verify your NoCTF email address",
            Body = $"Verify your NoCTF account by opening this link:\n\n{verificationUrl}\n\n" +
                   "If you did not create this account, you can ignore this message.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));

        await SendAsync(message, smtp, ct);
    }

    public async Task SendTestAsync(string recipient, CancellationToken ct)
    {
        var smtp = (await settingsStore.GetAsync(ct)).Smtp;
        using var message = new MailMessage
        {
            From = new MailAddress(smtp.FromAddress, smtp.FromName),
            Subject = "NoCTF SMTP configuration test",
            Body = "NoCTF successfully connected to the configured SMTP server and sent this test message.",
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(recipient));
        await SendAsync(message, smtp, ct);
    }

    private static async Task SendAsync(MailMessage message, SmtpOptions smtp, CancellationToken ct)
    {
        using var client = new SmtpClient(smtp.Host, smtp.Port)
        {
            EnableSsl = smtp.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Timeout = checked(smtp.TimeoutSeconds * 1000)
        };
        if (!string.IsNullOrWhiteSpace(smtp.UserName))
            client.Credentials = new NetworkCredential(smtp.UserName, smtp.Password);
        await client.SendMailAsync(message, ct);
    }
}

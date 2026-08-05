using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EmailVerificationConfigurationStore(
    NoCtfDbContext db,
    EmailVerificationSecretProtector secrets)
    : IEmailVerificationConfigurationStore,
        IEmailVerificationDeliveryConfigurationReader
{
    private const short SettingsId = 1;

    public async Task<EmailVerificationConfigurationView> GetAsync(CancellationToken ct) =>
        ToView(await db.EmailVerificationSettings.AsNoTracking()
            .SingleAsync(settings => settings.Id == SettingsId, ct));

    public async Task<EmailVerificationConfigurationView?> UpdateAsync(
        UpdateEmailVerificationConfigurationCommand command,
        CancellationToken ct)
    {
        var updated = await db.EmailVerificationSettings
            .Where(settings => settings.Id == SettingsId
                && settings.Revision == command.ExpectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(settings => settings.Enabled, command.Enabled)
                .SetProperty(settings => settings.PublicBaseUrl, command.PublicBaseUrl.TrimEnd('/'))
                .SetProperty(settings => settings.TokenLifetimeMinutes, command.TokenLifetimeMinutes)
                .SetProperty(settings => settings.ResendCooldownSeconds, command.ResendCooldownSeconds)
                .SetProperty(settings => settings.SmtpHost, command.SmtpHost.Trim())
                .SetProperty(settings => settings.SmtpPort, command.SmtpPort)
                .SetProperty(settings => settings.SmtpSecurityMode, command.SmtpSecurityMode)
                .SetProperty(
                    settings => settings.SmtpEnableSsl,
                    command.SmtpSecurityMode != SmtpSecurityMode.None)
                .SetProperty(settings => settings.SmtpUserName, command.SmtpUserName.Trim())
                .SetProperty(settings => settings.SmtpFromAddress, command.SmtpFromAddress.Trim())
                .SetProperty(settings => settings.SmtpFromName, command.SmtpFromName.Trim())
                .SetProperty(settings => settings.SmtpTimeoutSeconds, command.SmtpTimeoutSeconds)
                .SetProperty(settings => settings.Revision, settings => settings.Revision + 1)
                .SetProperty(settings => settings.UpdatedAt, command.Now),
                ct);
        return updated == 1 ? await GetAsync(ct) : null;
    }

    public async Task<EmailVerificationConfigurationView?> ReplacePasswordAsync(
        long expectedRevision,
        string password,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ciphertext = secrets.Protect(password);
        var updated = await db.EmailVerificationSettings
            .Where(settings => settings.Id == SettingsId
                && settings.Revision == expectedRevision)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(settings => settings.SmtpPasswordCiphertext, ciphertext)
                .SetProperty(settings => settings.Revision, settings => settings.Revision + 1)
                .SetProperty(settings => settings.UpdatedAt, now),
                ct);
        return updated == 1 ? await GetAsync(ct) : null;
    }

    public async Task<EmailVerificationDeliveryConfiguration?> GetDeliveryConfigurationAsync(
        bool requireEnabled,
        CancellationToken ct)
    {
        var settings = await db.EmailVerificationSettings.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == SettingsId, ct);
        var usesAuthentication = !string.IsNullOrWhiteSpace(settings.SmtpUserName);
        if ((requireEnabled && !settings.Enabled)
            || (usesAuthentication && settings.SmtpPasswordCiphertext is null)
            || string.IsNullOrWhiteSpace(settings.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.SmtpFromAddress))
        {
            return null;
        }

        return new(
            settings.Enabled,
            settings.PublicBaseUrl,
            settings.SmtpHost,
            settings.SmtpPort,
            ResolveSecurityMode(settings),
            settings.SmtpUserName,
            usesAuthentication
                ? secrets.Unprotect(settings.SmtpPasswordCiphertext!)
                : null,
            settings.SmtpFromAddress,
            settings.SmtpFromName,
            settings.SmtpTimeoutSeconds);
    }

    private static EmailVerificationConfigurationView ToView(
        EmailVerificationSettings settings) =>
        new(
            settings.Enabled,
            settings.PublicBaseUrl,
            settings.TokenLifetimeMinutes,
            settings.ResendCooldownSeconds,
            settings.SmtpHost,
            settings.SmtpPort,
            ResolveSecurityMode(settings),
            settings.SmtpUserName,
            settings.SmtpPasswordCiphertext is { Length: > 0 },
            settings.SmtpFromAddress,
            settings.SmtpFromName,
            settings.SmtpTimeoutSeconds,
            settings.Revision,
            settings.UpdatedAt);

    private static SmtpSecurityMode ResolveSecurityMode(EmailVerificationSettings settings) =>
        settings.SmtpSecurityMode
        ?? (settings.SmtpEnableSsl
            ? settings.SmtpPort == 465
                ? SmtpSecurityMode.SslOnConnect
                : SmtpSecurityMode.StartTls
            : SmtpSecurityMode.None);
}

using Microsoft.EntityFrameworkCore;
using NoCTF.Application.Authentication.EmailVerification;
using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Authentication;

public sealed class EmailVerificationConfigurationStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets)
    : IEmailVerificationConfigurationStore,
        IEmailVerificationDeliveryConfigurationReader
{
    private const short SettingsId = 1;

    public Task<EmailVerificationConfigurationView> GetAsync(CancellationToken ct) =>
        db.PlatformSettings.AsNoTracking().IgnoreAutoIncludes().Where(settings => settings.Id == SettingsId)
            .Select(settings => new EmailVerificationConfigurationView(
                settings.EmailVerificationEnabled, settings.PublicBaseUrl,
                settings.EmailVerificationTokenLifetimeMinutes, settings.EmailVerificationResendCooldownSeconds,
                settings.EmailPasswordResetTokenLifetimeMinutes, settings.EmailPasswordResetCooldownSeconds,
                settings.EmailPasswordResetMaxRequestsPerHour, settings.EmailSmtpHost, settings.EmailSmtpPort,
                settings.EmailSmtpSecurityMode ?? SmtpSecurityMode.None, settings.EmailSmtpUserName,
                settings.EmailSmtpPasswordCiphertext != null && settings.EmailSmtpPasswordCiphertext.Length > 0,
                settings.EmailSmtpFromAddress, settings.EmailSmtpFromName, settings.EmailSmtpTimeoutSeconds, settings.UpdatedAt))
            .SingleAsync(ct);

    public async Task<EmailVerificationConfigurationView> UpdateAsync(
        UpdateEmailVerificationConfigurationCommand command,
        CancellationToken ct)
    {
        await db.PlatformSettings
            .Where(settings => settings.Id == SettingsId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(settings => settings.EmailVerificationEnabled, command.Enabled)
                .SetProperty(settings => settings.EmailVerificationTokenLifetimeMinutes, command.TokenLifetimeMinutes)
                .SetProperty(settings => settings.EmailVerificationResendCooldownSeconds, command.ResendCooldownSeconds)
                .SetProperty(
                    settings => settings.EmailPasswordResetTokenLifetimeMinutes,
                    command.PasswordResetTokenLifetimeMinutes)
                .SetProperty(
                    settings => settings.EmailPasswordResetCooldownSeconds,
                    command.PasswordResetCooldownSeconds)
                .SetProperty(
                    settings => settings.EmailPasswordResetMaxRequestsPerHour,
                    command.PasswordResetMaxRequestsPerHour)
                .SetProperty(settings => settings.EmailSmtpHost, command.SmtpHost.Trim())
                .SetProperty(settings => settings.EmailSmtpPort, command.SmtpPort)
                .SetProperty(settings => settings.EmailSmtpSecurityMode, command.SmtpSecurityMode)
                .SetProperty(settings => settings.EmailSmtpUserName, command.SmtpUserName.Trim())
                .SetProperty(settings => settings.EmailSmtpFromAddress, command.SmtpFromAddress.Trim())
                .SetProperty(settings => settings.EmailSmtpFromName, command.SmtpFromName.Trim())
                .SetProperty(settings => settings.EmailSmtpTimeoutSeconds, command.SmtpTimeoutSeconds)
                .SetProperty(settings => settings.UpdatedAt, command.Now),
                ct);
        return await GetAsync(ct);
    }

    public async Task<EmailVerificationConfigurationView> ReplacePasswordAsync(
        string password,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ciphertext = secrets.Protect(
            password,
            PlatformSecretPurpose.EmailSmtpPassword);
        await db.PlatformSettings
            .Where(settings => settings.Id == SettingsId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(settings => settings.EmailSmtpPasswordCiphertext, ciphertext)
                .SetProperty(settings => settings.UpdatedAt, now),
                ct);
        return await GetAsync(ct);
    }

    public async Task<EmailVerificationDeliveryConfiguration?> GetDeliveryConfigurationAsync(
        bool requireEnabled,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.AsNoTracking()
            .AsSplitQuery()
            .SingleAsync(candidate => candidate.Id == SettingsId, ct);
        return ToDeliveryConfiguration(settings, secrets, requireEnabled);
    }

    internal static EmailVerificationDeliveryConfiguration? ToDeliveryConfiguration(
        PlatformSettings settings,
        PlatformSecretProtector secrets,
        bool requireEnabled)
    {
        var usesAuthentication = !string.IsNullOrWhiteSpace(settings.EmailSmtpUserName);
        if ((requireEnabled && !settings.EmailVerificationEnabled)
            || (usesAuthentication && settings.EmailSmtpPasswordCiphertext is null)
            || string.IsNullOrWhiteSpace(settings.EmailSmtpHost)
            || string.IsNullOrWhiteSpace(settings.EmailSmtpFromAddress))
        {
            return null;
        }

        return new(
            settings.EmailVerificationEnabled,
            settings.PublicBaseUrl,
            settings.EmailSmtpHost,
            settings.EmailSmtpPort,
            ResolveSecurityMode(settings),
            settings.EmailSmtpUserName,
            usesAuthentication
                ? secrets.Unprotect(
                    settings.EmailSmtpPasswordCiphertext!,
                    PlatformSecretPurpose.EmailSmtpPassword)
                : null,
            settings.EmailSmtpFromAddress,
            settings.EmailSmtpFromName,
            settings.EmailSmtpTimeoutSeconds);
    }

    private static SmtpSecurityMode ResolveSecurityMode(PlatformSettings settings) =>
        settings.EmailSmtpSecurityMode ?? SmtpSecurityMode.None;
}

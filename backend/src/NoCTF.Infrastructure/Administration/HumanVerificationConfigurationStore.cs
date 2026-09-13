using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Persistence;

namespace NoCTF.Infrastructure.Administration;

public sealed class HumanVerificationConfigurationStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    IOptions<HumanVerificationOptions> deploymentOptions)
    : IHumanVerificationConfigurationStore,
        IHumanVerificationConfigurationReader
{
    private const short SettingsId = 1;

    public async Task<HumanVerificationConfigurationView> GetAsync(
        CancellationToken ct) =>
        ToView(await LoadAsync(ct));

    public async Task<HumanVerificationConfigurationView> UpdateAsync(
        UpdateHumanVerificationConfigurationCommand command,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        SeedDeploymentSecrets(settings);
        settings.HumanVerificationEnabled = command.Enabled;
        settings.HumanVerificationRuntimeEnabled = command.RuntimeEnabled;
        settings.HumanVerificationEvaluationEnabled = command.EvaluationEnabled;
        settings.HumanVerificationProvider = command.Provider;
        settings.HumanVerificationCapServerUrl = command.CapServerUrl;
        settings.HumanVerificationCapSiteKey = command.CapSiteKey;
        settings.HumanVerificationTurnstileSiteKey = command.TurnstileSiteKey;
        settings.HumanVerificationTurnstileAllowedHostnames =
            command.TurnstileAllowedHostnames.ToArray();
        settings.UpdatedAt = command.Now;
        await db.SaveChangesAsync(ct);
        return ToView(settings);
    }

    public async Task<HumanVerificationConfigurationView> ReplaceSecretAsync(
        HumanVerificationProvider provider,
        string secret,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var settings = await db.PlatformSettings.SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);
        var ciphertext = secrets.Protect(secret, Purpose(provider));
        if (provider == HumanVerificationProvider.Cap)
            settings.HumanVerificationCapSecretCiphertext = ciphertext;
        else
            settings.HumanVerificationTurnstileSecretCiphertext = ciphertext;
        settings.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return ToView(settings);
    }

    public async Task<HumanVerificationRuntimeConfiguration> GetRuntimeConfigurationAsync(
        CancellationToken ct)
    {
        var settings = await LoadAsync(ct);
        if (settings.HumanVerificationProvider is null)
        {
            var fallback = deploymentOptions.Value;
            return new(
                settings.HumanVerificationEnabled
                    && fallback.Provider != HumanVerificationProvider.None,
                fallback,
                settings.HumanVerificationRuntimeEnabled,
                settings.HumanVerificationEvaluationEnabled);
        }

        var options = new HumanVerificationOptions
        {
            Provider = settings.HumanVerificationProvider.Value,
            Cap = new()
            {
                ServerUrl = settings.HumanVerificationCapServerUrl,
                SiteKey = settings.HumanVerificationCapSiteKey,
                Secret = settings.HumanVerificationCapSecretCiphertext is { Length: > 0 }
                    ? secrets.Unprotect(
                        settings.HumanVerificationCapSecretCiphertext,
                        PlatformSecretPurpose.HumanVerificationCapSecret)
                    : string.Empty
            },
            Turnstile = new()
            {
                SiteKey = settings.HumanVerificationTurnstileSiteKey,
                Secret = settings.HumanVerificationTurnstileSecretCiphertext is { Length: > 0 }
                    ? secrets.Unprotect(
                        settings.HumanVerificationTurnstileSecretCiphertext,
                        PlatformSecretPurpose.HumanVerificationTurnstileSecret)
                    : string.Empty,
                AllowedHostnames =
                    settings.HumanVerificationTurnstileAllowedHostnames.ToArray()
            }
        };
        return new(
            settings.HumanVerificationEnabled
                && options.Provider != HumanVerificationProvider.None,
            options,
            settings.HumanVerificationRuntimeEnabled,
            settings.HumanVerificationEvaluationEnabled);
    }

    private Task<PlatformSettings> LoadAsync(CancellationToken ct) =>
        db.PlatformSettings.AsNoTracking().SingleAsync(
            candidate => candidate.Id == SettingsId,
            ct);

    private HumanVerificationConfigurationView ToView(PlatformSettings settings)
    {
        var fallback = deploymentOptions.Value;
        var useDeployment = settings.HumanVerificationProvider is null;
        var provider = settings.HumanVerificationProvider ?? fallback.Provider;
        return new(
            settings.HumanVerificationEnabled
                && provider != HumanVerificationProvider.None,
            provider,
            false,
            useDeployment
                ? fallback.Cap.ServerUrl
                : settings.HumanVerificationCapServerUrl,
            useDeployment
                ? fallback.Cap.SiteKey
                : settings.HumanVerificationCapSiteKey,
            useDeployment
                ? !string.IsNullOrWhiteSpace(fallback.Cap.Secret)
                : settings.HumanVerificationCapSecretCiphertext is { Length: > 0 },
            useDeployment
                ? fallback.Turnstile.SiteKey
                : settings.HumanVerificationTurnstileSiteKey,
            useDeployment
                ? !string.IsNullOrWhiteSpace(fallback.Turnstile.Secret)
                : settings.HumanVerificationTurnstileSecretCiphertext is { Length: > 0 },
            useDeployment
                ? fallback.Turnstile.AllowedHostnames.ToArray()
                : settings.HumanVerificationTurnstileAllowedHostnames.ToArray(),
            settings.UpdatedAt,
            settings.HumanVerificationRuntimeEnabled,
            settings.HumanVerificationEvaluationEnabled);
    }

    private void SeedDeploymentSecrets(PlatformSettings settings)
    {
        if (settings.HumanVerificationProvider is not null)
            return;
        var fallback = deploymentOptions.Value;
        if (settings.HumanVerificationCapSecretCiphertext is null
            && !string.IsNullOrWhiteSpace(fallback.Cap.Secret))
        {
            settings.HumanVerificationCapSecretCiphertext = secrets.Protect(
                fallback.Cap.Secret,
                PlatformSecretPurpose.HumanVerificationCapSecret);
        }
        if (settings.HumanVerificationTurnstileSecretCiphertext is null
            && !string.IsNullOrWhiteSpace(fallback.Turnstile.Secret))
        {
            settings.HumanVerificationTurnstileSecretCiphertext = secrets.Protect(
                fallback.Turnstile.Secret,
                PlatformSecretPurpose.HumanVerificationTurnstileSecret);
        }
    }

    private static PlatformSecretPurpose Purpose(
        HumanVerificationProvider provider) => provider switch
        {
            HumanVerificationProvider.Cap =>
                PlatformSecretPurpose.HumanVerificationCapSecret,
            HumanVerificationProvider.Turnstile =>
                PlatformSecretPurpose.HumanVerificationTurnstileSecret,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
}

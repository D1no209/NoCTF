using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoCTF.Application.Admission;
using NoCTF.Domain.Platform;
using NoCTF.Infrastructure.Authentication;
using NoCTF.Infrastructure.Caching;
using NoCTF.Infrastructure.Persistence;
using ZiggyCreatures.Caching.Fusion;

namespace NoCTF.Infrastructure.Administration;

public sealed class HumanVerificationConfigurationStore(
    NoCtfDbContext db,
    PlatformSecretProtector secrets,
    IOptions<HumanVerificationOptions> deploymentOptions,
    IFusionCacheProvider? cacheProvider = null)
    : IHumanVerificationConfigurationStore,
        IHumanVerificationConfigurationReader
{
    private const short SettingsId = 1;
    private const string CacheKey = "human-verification-configuration";
    private readonly IFusionCache? cache = cacheProvider?.GetCache(NoCtfCacheNames.ReadModels);

    public async Task<HumanVerificationConfigurationView> GetAsync(
        CancellationToken ct) =>
        ToView(await GetSnapshotAsync(ct));

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
        var snapshot = HumanVerificationConfigurationSnapshot.From(settings);
        if (cache is not null)
            await cache.SetAsync(CacheKey, snapshot, token: ct);
        return ToView(snapshot);
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
        var snapshot = HumanVerificationConfigurationSnapshot.From(settings);
        if (cache is not null)
            await cache.SetAsync(CacheKey, snapshot, token: ct);
        return ToView(snapshot);
    }

    public async Task<HumanVerificationRuntimeConfiguration> GetRuntimeConfigurationAsync(
        CancellationToken ct)
    {
        var settings = await GetSnapshotAsync(ct);
        if (settings.Provider is null)
        {
            var fallback = deploymentOptions.Value;
            return new(
                settings.Enabled
                    && fallback.Provider != HumanVerificationProvider.None,
                fallback,
                settings.RuntimeEnabled,
                settings.EvaluationEnabled);
        }

        var options = new HumanVerificationOptions
        {
            Provider = settings.Provider.Value,
            Cap = new()
            {
                ServerUrl = settings.CapServerUrl,
                SiteKey = settings.CapSiteKey,
                Secret = settings.CapSecretCiphertext is { Length: > 0 }
                    ? secrets.Unprotect(
                        settings.CapSecretCiphertext,
                        PlatformSecretPurpose.HumanVerificationCapSecret)
                    : string.Empty
            },
            Turnstile = new()
            {
                SiteKey = settings.TurnstileSiteKey,
                Secret = settings.TurnstileSecretCiphertext is { Length: > 0 }
                    ? secrets.Unprotect(
                        settings.TurnstileSecretCiphertext,
                        PlatformSecretPurpose.HumanVerificationTurnstileSecret)
                    : string.Empty,
                AllowedHostnames =
                    settings.TurnstileAllowedHostnames.ToArray()
            }
        };
        return new(
            settings.Enabled
                && options.Provider != HumanVerificationProvider.None,
            options,
            settings.RuntimeEnabled,
            settings.EvaluationEnabled);
    }

    private Task<HumanVerificationConfigurationSnapshot> GetSnapshotAsync(
        CancellationToken ct) =>
        cache is null
            ? LoadAsync(ct)
            : cache.GetOrSetAsync<HumanVerificationConfigurationSnapshot>(
                CacheKey,
                (_, token) => LoadAsync(token),
                token: ct).AsTask();

    private async Task<HumanVerificationConfigurationSnapshot> LoadAsync(
        CancellationToken ct) =>
        HumanVerificationConfigurationSnapshot.From(
            await db.PlatformSettings.AsNoTracking().SingleAsync(
                candidate => candidate.Id == SettingsId,
                ct));

    private HumanVerificationConfigurationView ToView(
        HumanVerificationConfigurationSnapshot settings)
    {
        var fallback = deploymentOptions.Value;
        var useDeployment = settings.Provider is null;
        var provider = settings.Provider ?? fallback.Provider;
        return new(
            settings.Enabled
                && provider != HumanVerificationProvider.None,
            provider,
            false,
            useDeployment
                ? fallback.Cap.ServerUrl
                : settings.CapServerUrl,
            useDeployment
                ? fallback.Cap.SiteKey
                : settings.CapSiteKey,
            useDeployment
                ? !string.IsNullOrWhiteSpace(fallback.Cap.Secret)
                : settings.CapSecretCiphertext is { Length: > 0 },
            useDeployment
                ? fallback.Turnstile.SiteKey
                : settings.TurnstileSiteKey,
            useDeployment
                ? !string.IsNullOrWhiteSpace(fallback.Turnstile.Secret)
                : settings.TurnstileSecretCiphertext is { Length: > 0 },
            useDeployment
                ? fallback.Turnstile.AllowedHostnames.ToArray()
                : settings.TurnstileAllowedHostnames.ToArray(),
            settings.UpdatedAt,
            settings.RuntimeEnabled,
            settings.EvaluationEnabled);
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

internal sealed record HumanVerificationConfigurationSnapshot(
    bool Enabled,
    bool RuntimeEnabled,
    bool EvaluationEnabled,
    HumanVerificationProvider? Provider,
    string CapServerUrl,
    string CapSiteKey,
    byte[]? CapSecretCiphertext,
    string TurnstileSiteKey,
    byte[]? TurnstileSecretCiphertext,
    string[] TurnstileAllowedHostnames,
    DateTimeOffset UpdatedAt)
{
    public static HumanVerificationConfigurationSnapshot From(
        PlatformSettings settings) => new(
        settings.HumanVerificationEnabled,
        settings.HumanVerificationRuntimeEnabled,
        settings.HumanVerificationEvaluationEnabled,
        settings.HumanVerificationProvider,
        settings.HumanVerificationCapServerUrl,
        settings.HumanVerificationCapSiteKey,
        settings.HumanVerificationCapSecretCiphertext,
        settings.HumanVerificationTurnstileSiteKey,
        settings.HumanVerificationTurnstileSecretCiphertext,
        settings.HumanVerificationTurnstileAllowedHostnames.ToArray(),
        settings.UpdatedAt);
}

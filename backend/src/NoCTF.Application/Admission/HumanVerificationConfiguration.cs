using NoCTF.Domain.Platform;

namespace NoCTF.Application.Admission;

public static class HumanVerificationConfigurationRules
{
    public const int MaximumUrlLength = 2048;
    public const int MaximumSiteKeyLength = 256;
    public const int MaximumSecretLength = 4096;
    public const int MaximumAllowedHostnames = 32;
}

public sealed record HumanVerificationConfigurationView(
    bool Enabled,
    HumanVerificationProvider Provider,
    bool Ready,
    string CapServerUrl,
    string CapSiteKey,
    bool CapSecretConfigured,
    string TurnstileSiteKey,
    bool TurnstileSecretConfigured,
    IReadOnlyList<string> TurnstileAllowedHostnames,
    DateTimeOffset UpdatedAt,
    bool RuntimeEnabled = true,
    bool EvaluationEnabled = true);

public sealed record UpdateHumanVerificationConfigurationCommand(
    bool Enabled,
    HumanVerificationProvider Provider,
    string CapServerUrl,
    string CapSiteKey,
    string TurnstileSiteKey,
    IReadOnlyList<string> TurnstileAllowedHostnames,
    DateTimeOffset Now,
    bool RuntimeEnabled = true,
    bool EvaluationEnabled = true);

public sealed record HumanVerificationRuntimeConfiguration(
    bool Enabled,
    HumanVerificationOptions Options,
    bool RuntimeEnabled = true,
    bool EvaluationEnabled = true)
{
    public bool IsRequired(HumanVerificationAction action) => Enabled && action switch
    {
        HumanVerificationAction.Runtime => RuntimeEnabled,
        HumanVerificationAction.Evaluation => EvaluationEnabled,
        _ => true
    };
}

public enum HumanVerificationConfigurationError
{
    ProviderInvalid,
    ProviderRequired,
    CapServerUrlInvalid,
    CapSiteKeyInvalid,
    CapSecretRequired,
    TurnstileSiteKeyInvalid,
    TurnstileSecretRequired,
    TurnstileAllowedHostnamesInvalid,
    SecretInvalid,
    CapConfigurationInvalid,
    CapProviderUnavailable
}

public enum CapHumanVerificationConfigurationProbeResult
{
    Succeeded,
    ConfigurationInvalid,
    ProviderUnavailable
}

public interface ICapHumanVerificationConfigurationProbe
{
    Task<CapHumanVerificationConfigurationProbeResult> ProbeAsync(
        HumanVerificationRuntimeConfiguration configuration,
        CancellationToken cancellationToken);
}

public enum HumanVerificationConfigurationUpdateState
{
    Updated,
    Invalid
}

public sealed record HumanVerificationConfigurationMutationResult(
    HumanVerificationConfigurationUpdateState State,
    HumanVerificationConfigurationView? Configuration = null,
    IReadOnlyList<HumanVerificationConfigurationError>? ValidationErrors = null)
{
    public IReadOnlyList<HumanVerificationConfigurationError> Errors =>
        ValidationErrors ?? [];
}

public interface IHumanVerificationConfigurationStore
{
    Task<HumanVerificationConfigurationView> GetAsync(
        CancellationToken cancellationToken);

    Task<HumanVerificationConfigurationView> UpdateAsync(
        UpdateHumanVerificationConfigurationCommand command,
        CancellationToken cancellationToken);

    Task<HumanVerificationConfigurationView> ReplaceSecretAsync(
        HumanVerificationProvider provider,
        string secret,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface IHumanVerificationConfigurationReader
{
    Task<HumanVerificationRuntimeConfiguration> GetRuntimeConfigurationAsync(
        CancellationToken cancellationToken);
}

public sealed record HumanVerificationValidationPolicy(bool Development);

public sealed class ManageHumanVerificationConfiguration(
    IHumanVerificationConfigurationStore store,
    HumanVerificationValidationPolicy policy,
    IHumanVerificationConfigurationReader? configurationReader = null,
    ICapHumanVerificationConfigurationProbe? capProbe = null)
{
    public async Task<HumanVerificationConfigurationView> GetAsync(
        CancellationToken ct = default) =>
        WithReadiness(await store.GetAsync(ct));

    public async Task<HumanVerificationConfigurationMutationResult> UpdateAsync(
        UpdateHumanVerificationConfigurationCommand command,
        CancellationToken ct = default)
    {
        if (!Enum.IsDefined(command.Provider))
        {
            return new(
                HumanVerificationConfigurationUpdateState.Invalid,
                ValidationErrors: [HumanVerificationConfigurationError.ProviderInvalid]);
        }

        var current = await GetAsync(ct);
        var normalized = Normalize(command);
        var candidate = new HumanVerificationConfigurationView(
            normalized.Enabled && normalized.Provider != HumanVerificationProvider.None,
            normalized.Provider,
            false,
            normalized.CapServerUrl,
            normalized.CapSiteKey,
            current.CapSecretConfigured,
            normalized.TurnstileSiteKey,
            current.TurnstileSecretConfigured,
            normalized.TurnstileAllowedHostnames,
            normalized.Now,
            normalized.RuntimeEnabled,
            normalized.EvaluationEnabled);
        var errors = Validate(candidate);
        if (errors.Count > 0)
        {
            return new(
                HumanVerificationConfigurationUpdateState.Invalid,
                ValidationErrors: errors);
        }

        if (candidate.Enabled && candidate.Provider == HumanVerificationProvider.Cap)
        {
            var probeError = await ProbeCapAsync(
                normalized.CapServerUrl,
                normalized.CapSiteKey,
                secretOverride: null,
                ct);
            if (probeError is not null)
            {
                return new(
                    HumanVerificationConfigurationUpdateState.Invalid,
                    ValidationErrors: [probeError.Value]);
            }
        }

        var updated = await store.UpdateAsync(normalized, ct);
        return new(
            HumanVerificationConfigurationUpdateState.Updated,
            WithReadiness(updated));
    }

    public async Task<HumanVerificationConfigurationMutationResult> ReplaceSecretAsync(
        HumanVerificationProvider provider,
        string secret,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (provider == HumanVerificationProvider.None
            || !Enum.IsDefined(provider)
            || string.IsNullOrWhiteSpace(secret)
            || secret.Length > HumanVerificationConfigurationRules.MaximumSecretLength)
        {
            return new(
                HumanVerificationConfigurationUpdateState.Invalid,
                ValidationErrors: [HumanVerificationConfigurationError.SecretInvalid]);
        }

        var current = await GetAsync(ct);
        if (provider == HumanVerificationProvider.Cap
            && current.Enabled
            && current.Provider == HumanVerificationProvider.Cap)
        {
            var probeError = await ProbeCapAsync(
                current.CapServerUrl,
                current.CapSiteKey,
                secret,
                ct);
            if (probeError is not null)
            {
                return new(
                    HumanVerificationConfigurationUpdateState.Invalid,
                    ValidationErrors: [probeError.Value]);
            }
        }

        var updated = await store.ReplaceSecretAsync(provider, secret, now, ct);
        return new(
            HumanVerificationConfigurationUpdateState.Updated,
            WithReadiness(updated));
    }

    private UpdateHumanVerificationConfigurationCommand Normalize(
        UpdateHumanVerificationConfigurationCommand command) =>
        command with
        {
            Enabled = command.Enabled && command.Provider != HumanVerificationProvider.None,
            CapServerUrl = command.CapServerUrl.Trim().TrimEnd('/'),
            CapSiteKey = command.CapSiteKey.Trim(),
            TurnstileSiteKey = command.TurnstileSiteKey.Trim(),
            TurnstileAllowedHostnames = command.TurnstileAllowedHostnames
                .Select(hostname => hostname.Trim().TrimEnd('.').ToLowerInvariant())
                .Where(hostname => hostname.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        };

    private HumanVerificationConfigurationView WithReadiness(
        HumanVerificationConfigurationView view) =>
        view with
        {
            Ready = Validate(
                view with { Enabled = true },
                validateInactiveProviderValues: false).Count == 0
        };

    private async Task<HumanVerificationConfigurationError?> ProbeCapAsync(
        string serverUrl,
        string siteKey,
        string? secretOverride,
        CancellationToken ct)
    {
        if (configurationReader is null || capProbe is null)
            return HumanVerificationConfigurationError.CapProviderUnavailable;

        var current = await configurationReader.GetRuntimeConfigurationAsync(ct);
        var cap = current.Options.Cap;
        var candidate = new HumanVerificationRuntimeConfiguration(
            true,
            new HumanVerificationOptions
            {
                Provider = HumanVerificationProvider.Cap,
                Cap = new CapHumanVerificationOptions
                {
                    ServerUrl = serverUrl,
                    BackendServerUrl = cap.BackendServerUrl,
                    SiteKey = siteKey,
                    Secret = secretOverride ?? cap.Secret,
                    ManagementApiKey = cap.ManagementApiKey
                }
            });
        return await capProbe.ProbeAsync(candidate, ct) switch
        {
            CapHumanVerificationConfigurationProbeResult.Succeeded => null,
            CapHumanVerificationConfigurationProbeResult.ConfigurationInvalid =>
                HumanVerificationConfigurationError.CapConfigurationInvalid,
            _ => HumanVerificationConfigurationError.CapProviderUnavailable
        };
    }

    private IReadOnlyList<HumanVerificationConfigurationError> Validate(
        HumanVerificationConfigurationView configuration,
        bool validateInactiveProviderValues = true)
    {
        var errors = new List<HumanVerificationConfigurationError>();
        if ((validateInactiveProviderValues
                || configuration.Provider == HumanVerificationProvider.Cap)
            && configuration.CapServerUrl.Length
            > HumanVerificationConfigurationRules.MaximumUrlLength)
            errors.Add(HumanVerificationConfigurationError.CapServerUrlInvalid);
        if ((validateInactiveProviderValues
                || configuration.Provider == HumanVerificationProvider.Cap)
            && configuration.CapSiteKey.Length
            > HumanVerificationConfigurationRules.MaximumSiteKeyLength)
            errors.Add(HumanVerificationConfigurationError.CapSiteKeyInvalid);
        if ((validateInactiveProviderValues
                || configuration.Provider == HumanVerificationProvider.Turnstile)
            && configuration.TurnstileSiteKey.Length
            > HumanVerificationConfigurationRules.MaximumSiteKeyLength)
            errors.Add(HumanVerificationConfigurationError.TurnstileSiteKeyInvalid);
        if ((validateInactiveProviderValues
                || configuration.Provider == HumanVerificationProvider.Turnstile)
            && configuration.TurnstileAllowedHostnames.Count
            > HumanVerificationConfigurationRules.MaximumAllowedHostnames)
            errors.Add(HumanVerificationConfigurationError.TurnstileAllowedHostnamesInvalid);

        switch (configuration.Provider)
        {
            case HumanVerificationProvider.None:
                if (configuration.Enabled)
                    errors.Add(HumanVerificationConfigurationError.ProviderRequired);
                break;
            case HumanVerificationProvider.Cap:
                if (!string.IsNullOrEmpty(configuration.CapServerUrl)
                    && !ValidCapServerUrl(configuration.CapServerUrl))
                    errors.Add(HumanVerificationConfigurationError.CapServerUrlInvalid);
                if (configuration.Enabled)
                {
                    if (!ValidCapServerUrl(configuration.CapServerUrl))
                        errors.Add(HumanVerificationConfigurationError.CapServerUrlInvalid);
                    if (string.IsNullOrEmpty(configuration.CapSiteKey))
                        errors.Add(HumanVerificationConfigurationError.CapSiteKeyInvalid);
                    if (!configuration.CapSecretConfigured)
                        errors.Add(HumanVerificationConfigurationError.CapSecretRequired);
                }
                break;
            case HumanVerificationProvider.Turnstile:
                if (configuration.TurnstileAllowedHostnames.Any(hostname =>
                    !ValidHostname(hostname)))
                    errors.Add(HumanVerificationConfigurationError.TurnstileAllowedHostnamesInvalid);
                if (configuration.Enabled)
                {
                    if (string.IsNullOrEmpty(configuration.TurnstileSiteKey))
                        errors.Add(HumanVerificationConfigurationError.TurnstileSiteKeyInvalid);
                    if (!configuration.TurnstileSecretConfigured)
                        errors.Add(HumanVerificationConfigurationError.TurnstileSecretRequired);
                    if (configuration.TurnstileAllowedHostnames.Count == 0)
                        errors.Add(HumanVerificationConfigurationError.TurnstileAllowedHostnamesInvalid);
                }
                break;
            default:
                errors.Add(HumanVerificationConfigurationError.ProviderInvalid);
                break;
        }
        return errors.Distinct().ToArray();
    }

    private bool ValidCapServerUrl(string value)
    {
        if (value.Length > HumanVerificationConfigurationRules.MaximumUrlLength
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;
        return policy.Development || uri.Scheme == Uri.UriSchemeHttps;
    }

    private bool ValidHostname(string value) =>
        value.Length is > 0 and <= 253
        && Uri.CheckHostName(value) != UriHostNameType.Unknown
        && (policy.Development || !IsLoopbackHost(value));

    private static bool IsLoopbackHost(string hostname) =>
        string.Equals(hostname, "localhost", StringComparison.OrdinalIgnoreCase)
        || (System.Net.IPAddress.TryParse(hostname, out var address)
            && System.Net.IPAddress.IsLoopback(address));
}

public sealed class NoOpHumanVerificationConfigurationStore
    : IHumanVerificationConfigurationStore,
        IHumanVerificationConfigurationReader
{
    private static readonly HumanVerificationConfigurationView Default = new(
        false,
        HumanVerificationProvider.None,
        false,
        string.Empty,
        string.Empty,
        false,
        string.Empty,
        false,
        [],
        DateTimeOffset.UnixEpoch,
        RuntimeEnabled: true,
        EvaluationEnabled: true);

    public Task<HumanVerificationConfigurationView> GetAsync(
        CancellationToken cancellationToken) => Task.FromResult(Default);

    public Task<HumanVerificationConfigurationView> UpdateAsync(
        UpdateHumanVerificationConfigurationCommand command,
        CancellationToken cancellationToken) => Task.FromResult(Default with
        {
            Enabled = command.Enabled,
            Provider = command.Provider,
            CapServerUrl = command.CapServerUrl,
            CapSiteKey = command.CapSiteKey,
            TurnstileSiteKey = command.TurnstileSiteKey,
            TurnstileAllowedHostnames = command.TurnstileAllowedHostnames,
            UpdatedAt = command.Now,
            RuntimeEnabled = command.RuntimeEnabled,
            EvaluationEnabled = command.EvaluationEnabled
        });

    public Task<HumanVerificationConfigurationView> ReplaceSecretAsync(
        HumanVerificationProvider provider,
        string secret,
        DateTimeOffset now,
        CancellationToken cancellationToken) => Task.FromResult(Default with
        {
            CapSecretConfigured = provider == HumanVerificationProvider.Cap,
            TurnstileSecretConfigured = provider == HumanVerificationProvider.Turnstile,
            UpdatedAt = now
        });

    public Task<HumanVerificationRuntimeConfiguration> GetRuntimeConfigurationAsync(
        CancellationToken cancellationToken) => Task.FromResult(
        new HumanVerificationRuntimeConfiguration(
            false,
            new HumanVerificationOptions(),
            Default.RuntimeEnabled,
            Default.EvaluationEnabled));
}

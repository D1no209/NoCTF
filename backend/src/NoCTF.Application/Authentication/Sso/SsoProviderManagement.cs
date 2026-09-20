using NoCTF.Domain.Identity;
using NoCTF.Domain.Platform;

namespace NoCTF.Application.Authentication.Sso;

public sealed record OidcSsoProviderDraft(
    string Issuer,
    string DiscoveryUrl,
    string ClientId,
    IReadOnlyList<string> Scopes,
    bool ReadUserInfo,
    string DisplayNameClaim);

public sealed record CasSsoProviderDraft(
    string IdentityNamespace,
    string LoginUrl,
    string ServiceValidateUrl,
    string DisplayNameAttribute);

public sealed record SsoProviderDraft(
    string Name,
    SsoProtocol Protocol,
    bool Enabled,
    bool AllowLogin,
    bool AllowBinding,
    int TimeoutSeconds,
    IReadOnlyList<string> AllowedHosts,
    OidcSsoProviderDraft? Oidc,
    CasSsoProviderDraft? Cas);

public sealed record OidcSsoProviderView(
    string Issuer,
    string DiscoveryUrl,
    string ClientId,
    IReadOnlyList<string> Scopes,
    bool ReadUserInfo,
    string DisplayNameClaim,
    bool SecretConfigured);

public sealed record CasSsoProviderView(
    string IdentityNamespace,
    string LoginUrl,
    string ServiceValidateUrl,
    string DisplayNameAttribute);

public sealed record SsoProviderView(
    Guid Id,
    string Name,
    SsoProtocol Protocol,
    bool Enabled,
    bool AllowLogin,
    bool AllowBinding,
    int TimeoutSeconds,
    IReadOnlyList<string> AllowedHosts,
    OidcSsoProviderView? Oidc,
    CasSsoProviderView? Cas);

public sealed record SsoConfigurationView(
    bool Enabled,
    string PublicBaseUrl,
    IReadOnlyList<SsoProviderView> Providers,
    DateTimeOffset UpdatedAt);

public enum SsoConfigurationMutationState
{
    Updated,
    InvalidConfiguration,
    ProviderNotFound,
    ProviderLimitReached,
    DuplicateTrustBoundary,
    TrustBoundaryImmutable,
    SecretRequired
}

public sealed record SsoConfigurationMutationResult(
    SsoConfigurationMutationState State,
    SsoConfigurationView? Configuration = null,
    Guid? ProviderId = null);

public enum SsoProviderAuditAction
{
    GlobalConfigurationUpdated,
    ProviderCreated,
    ProviderUpdated,
    ProviderSecretReplaced
}

public sealed record SsoProviderAuditFact(
    int SchemaVersion,
    SsoProviderAuditAction Action,
    Guid? ProviderId,
    string? ProviderName,
    SsoProtocol? Protocol);

public interface ISsoConfigurationStore
{
    Task<SsoConfigurationView> GetAsync(CancellationToken cancellationToken);

    Task<SsoConfigurationMutationResult> UpdateGlobalAsync(
        bool enabled,
        string publicBaseUrl,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SsoConfigurationMutationResult> CreateProviderAsync(
        Guid providerId,
        SsoProviderDraft provider,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SsoConfigurationMutationResult> UpdateProviderAsync(
        Guid providerId,
        SsoProviderDraft provider,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task<SsoConfigurationMutationResult> ReplaceSecretAsync(
        Guid providerId,
        string secret,
        Guid actorUserId,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}

public interface ISsoProviderConnectionTester
{
    Task<SsoProviderConnectionTestResult> TestAsync(
        Guid providerId,
        CancellationToken cancellationToken);
}

public sealed record SsoProviderConnectionTestResult(
    bool Succeeded,
    SsoProtocol? Protocol,
    string? Issuer,
    string? FailureStage,
    string? FailureCode);

public sealed class ManageSsoProviders(
    ISsoConfigurationStore store,
    ISsoProviderConnectionTester connections,
    TimeProvider clock)
{
    public Task<SsoConfigurationView> GetAsync(CancellationToken ct = default) =>
        store.GetAsync(ct);

    public Task<SsoConfigurationMutationResult> UpdateGlobalAsync(
        bool enabled,
        string publicBaseUrl,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.UpdateGlobalAsync(
            enabled,
            publicBaseUrl.TrimEnd('/'),
            actorUserId,
            clock.GetUtcNow(),
            ct);

    public Task<SsoConfigurationMutationResult> CreateProviderAsync(
        SsoProviderDraft provider,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.CreateProviderAsync(
            Guid.CreateVersion7(clock.GetUtcNow()),
            Normalize(provider),
            actorUserId,
            clock.GetUtcNow(),
            ct);

    public Task<SsoConfigurationMutationResult> UpdateProviderAsync(
        Guid providerId,
        SsoProviderDraft provider,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.UpdateProviderAsync(
            providerId,
            Normalize(provider),
            actorUserId,
            clock.GetUtcNow(),
            ct);

    public Task<SsoConfigurationMutationResult> ReplaceSecretAsync(
        Guid providerId,
        string secret,
        Guid actorUserId,
        CancellationToken ct = default) =>
        store.ReplaceSecretAsync(
            providerId,
            secret,
            actorUserId,
            clock.GetUtcNow(),
            ct);

    public Task<SsoProviderConnectionTestResult> TestConnectionAsync(
        Guid providerId,
        CancellationToken ct = default) =>
        connections.TestAsync(providerId, ct);

    private static SsoProviderDraft Normalize(SsoProviderDraft provider) => provider with
    {
        Name = provider.Name.Trim(),
        AllowedHosts = provider.AllowedHosts
            .Select(host => host.Trim().TrimEnd('.').ToLowerInvariant())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray(),
        Oidc = provider.Oidc is null ? null : provider.Oidc with
        {
            Issuer = provider.Oidc.Issuer.TrimEnd('/'),
            DiscoveryUrl = provider.Oidc.DiscoveryUrl.Trim(),
            ClientId = provider.Oidc.ClientId.Trim(),
            Scopes = provider.Oidc.Scopes.Select(scope => scope.Trim())
                .Where(scope => scope.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            DisplayNameClaim = provider.Oidc.DisplayNameClaim.Trim()
        },
        Cas = provider.Cas is null ? null : provider.Cas with
        {
            IdentityNamespace = provider.Cas.IdentityNamespace.Trim(),
            LoginUrl = provider.Cas.LoginUrl.Trim(),
            ServiceValidateUrl = provider.Cas.ServiceValidateUrl.Trim(),
            DisplayNameAttribute = provider.Cas.DisplayNameAttribute.Trim()
        }
    };
}

public static class SsoProviderValidation
{
    public static bool IsValidPublicBaseUrl(string value, bool allowHttp) =>
        IsSafeBaseUri(value, allowHttp, out _);

    public static bool IsValid(SsoProviderDraft provider, bool allowHttp)
    {
        if (provider.Name.Length is < 1 or > SsoRules.MaximumProviderNameLength
            || provider.TimeoutSeconds is < SsoRules.MinimumTimeoutSeconds
                or > SsoRules.MaximumTimeoutSeconds
            || provider.AllowedHosts.Count is 0 or > SsoRules.MaximumAllowedHosts
            || provider.AllowedHosts.Any(host => !IsHost(host)))
            return false;

        return provider.Protocol switch
        {
            SsoProtocol.Oidc => provider.Cas is null
                && provider.Oidc is { } oidc
                && IsOidcValid(oidc, provider.AllowedHosts, allowHttp),
            SsoProtocol.Cas => provider.Oidc is null
                && provider.Cas is { } cas
                && IsCasValid(cas, provider.AllowedHosts, allowHttp),
            _ => false
        };
    }

    private static bool IsOidcValid(
        OidcSsoProviderDraft oidc,
        IReadOnlyList<string> allowedHosts,
        bool allowHttp)
    {
        if (oidc.ClientId.Length is < 1 or > SsoRules.MaximumClientIdLength
            || oidc.Scopes.Count is 0 or > SsoRules.MaximumScopes
            || !oidc.Scopes.Contains("openid", StringComparer.Ordinal)
            || oidc.Scopes.Any(scope => scope.Length is < 1 or > SsoRules.MaximumScopeLength)
            || oidc.DisplayNameClaim.Length is < 1 or > SsoRules.MaximumAttributeNameLength
            || !IsSafeBaseUri(oidc.Issuer, allowHttp, out var issuer)
            || !IsSafeEndpointUri(oidc.DiscoveryUrl, allowHttp, out var discovery))
            return false;
        return allowedHosts.Contains(issuer!.DnsSafeHost, StringComparer.OrdinalIgnoreCase)
            && allowedHosts.Contains(discovery!.DnsSafeHost, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsCasValid(
        CasSsoProviderDraft cas,
        IReadOnlyList<string> allowedHosts,
        bool allowHttp)
    {
        if (cas.IdentityNamespace.Length is < 1 or > SsoRules.MaximumIdentityNamespaceLength
            || cas.DisplayNameAttribute.Length is < 1 or > SsoRules.MaximumAttributeNameLength
            || !IsSafeEndpointUri(cas.LoginUrl, allowHttp, out var login)
            || !IsSafeEndpointUri(cas.ServiceValidateUrl, allowHttp, out var validation))
            return false;
        return allowedHosts.Contains(login!.DnsSafeHost, StringComparer.OrdinalIgnoreCase)
            && allowedHosts.Contains(validation!.DnsSafeHost, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsSafeBaseUri(string value, bool allowHttp, out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri)
            || !AllowedScheme(uri, allowHttp)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || uri.UserInfo.Length > 0)
            return false;
        return uri.AbsolutePath is "" or "/";
    }

    private static bool IsSafeEndpointUri(string value, bool allowHttp, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && AllowedScheme(uri, allowHttp)
        && string.IsNullOrEmpty(uri.Fragment)
        && uri.UserInfo.Length == 0;

    private static bool AllowedScheme(Uri uri, bool allowHttp) =>
        uri.Scheme == Uri.UriSchemeHttps
        || allowHttp && uri.Scheme == Uri.UriSchemeHttp;

    private static bool IsHost(string value) =>
        value.Length is >= 1 and <= 253
        && Uri.CheckHostName(value) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
}
